#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;

namespace OpenRA.Traits
{
	[Desc("Required for shroud and fog visibility checks. Add this to the player actor.")]
	public class ShroudInfo : ITraitInfo, ILobbyOptions
	{
		[Translate]
		[Desc("Descriptive label for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxLabel = "Fog of War";

		[Translate]
		[Desc("Tooltip description for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxDescription = "Line of sight is required to view enemy forces";

		[Desc("Default value of the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxEnabled = false;

		[Desc("Prevent the fog enabled state from being changed in the lobby.")]
		public readonly bool FogCheckboxLocked = false;

		[Desc("Whether to display the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxVisible = true;

		[Desc("Display order for the fog checkbox in the lobby.")]
		public readonly int FogCheckboxDisplayOrder = 0;

		[Translate]
		[Desc("Descriptive label for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxLabel = "Explored Map";

		[Translate]
		[Desc("Tooltip description for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxDescription = "Initial map shroud is revealed";

		[Desc("Default value of the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxEnabled = false;

		[Desc("Prevent the explore map enabled state from being changed in the lobby.")]
		public readonly bool ExploredMapCheckboxLocked = false;

		[Desc("Whether to display the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxVisible = true;

		[Desc("Display order for the explore map checkbox in the lobby.")]
		public readonly int ExploredMapCheckboxDisplayOrder = 0;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(Ruleset rules)
		{
			yield return new LobbyBooleanOption("explored", ExploredMapCheckboxLabel, ExploredMapCheckboxDescription,
				ExploredMapCheckboxVisible, ExploredMapCheckboxDisplayOrder, ExploredMapCheckboxEnabled, ExploredMapCheckboxLocked);
			yield return new LobbyBooleanOption("fog", FogCheckboxLabel, FogCheckboxDescription,
				FogCheckboxVisible, FogCheckboxDisplayOrder, FogCheckboxEnabled, FogCheckboxLocked);
		}

		public object Create(ActorInitializer init) { return new Shroud(init.Self, this); }
	}

	public class Shroud : ISync, INotifyCreated, ITick
	{
		public enum SourceType : byte { PassiveVisibility, Shroud, Visibility }
		public event Action<PPos> OnShroudChanged;

		enum ShroudCellType : byte { Shroud, Fog, Visible }
		class ShroudSource
		{
			public readonly SourceType Type;
			public readonly PPos[] ProjectedCells;

			public ShroudSource(SourceType type, PPos[] projectedCells)
			{
				Type = type;
				ProjectedCells = projectedCells;
			}
		}

		readonly Actor self;
		readonly ShroudInfo info;
		readonly Map map;

		// Individual shroud modifier sources (type and area)
		readonly Dictionary<object, ShroudSource> sources = new Dictionary<object, ShroudSource>();

		// Per-cell count of each source type, used to resolve the final cell type
		readonly CellLayer<short> passiveVisibleCount;
		readonly CellLayer<short> visibleCount;
		readonly CellLayer<short> generatedShroudCount;
		readonly CellLayer<bool> explored;
		readonly CellLayer<bool> touched;

		// Per-cell cache of the resolved cell type (shroud/fog/visible)
		readonly CellLayer<ShroudCellType> resolvedType;

		[Sync]
		bool disabled;
		public bool Disabled
		{
			get
			{
				return disabled;
			}

			set
			{
				if (disabled == value)
					return;

				disabled = value;
			}
		}

		bool fogEnabled;
		public bool FogEnabled { get { return !Disabled && fogEnabled; } }
		public bool ExploreMapEnabled { get; private set; }

		public int Hash { get; private set; }

		// Enabled at runtime on first use
		bool shroudGenerationEnabled;
		bool passiveVisibilityEnabled;

		public Shroud(Actor self, ShroudInfo info)
		{
			this.self = self;
			this.info = info;
			map = self.World.Map;

			passiveVisibleCount = new CellLayer<short>(map);
			visibleCount = new CellLayer<short>(map);
			generatedShroudCount = new CellLayer<short>(map);
			explored = new CellLayer<bool>(map);
			touched = new CellLayer<bool>(map);

			// Defaults to 0 = Shroud
			resolvedType = new CellLayer<ShroudCellType>(map);
		}

		void INotifyCreated.Created(Actor self)
		{
			var gs = self.World.LobbyInfo.GlobalSettings;
			fogEnabled = gs.OptionOrDefault("fog", info.FogCheckboxEnabled);

			ExploreMapEnabled = gs.OptionOrDefault("explored", info.ExploredMapCheckboxEnabled);
			if (ExploreMapEnabled)
				self.World.AddFrameEndTask(w => ExploreAll());
		}

		void ITick.Tick(Actor self)
		{
			if (OnShroudChanged == null)
				return;

			foreach (var puv in map.ProjectedCellBounds)
			{
				var uv = (MPos)puv;
				if (!touched[uv])
					continue;

				touched[uv] = false;

				var type = ShroudCellType.Shroud;

				if (explored[uv] && (!shroudGenerationEnabled || generatedShroudCount[uv] == 0 || visibleCount[uv] > 0))
				{
					var count = visibleCount[uv];
					if (passiveVisibilityEnabled)
						count += passiveVisibleCount[uv];

					type = count > 0 ? ShroudCellType.Visible : ShroudCellType.Fog;
				}

				var oldResolvedType = resolvedType[uv];
				resolvedType[uv] = type;
				if (type != oldResolvedType)
					OnShroudChanged((PPos)uv);
			}

			Hash = Sync.HashPlayer(self.Owner) + self.World.WorldTick;
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, WPos pos, WDist minRange, WDist maxRange, int maxHeightDelta = -1)
		{
			// Account for potential extra half-cell from odd-height terrain
			var r = (maxRange.Length + 1023 + 512) / 1024;
			var minLimit = minRange.LengthSquared;
			var maxLimit = maxRange.LengthSquared;

			// Project actor position into the shroud plane
			var projectedPos = pos - new WVec(0, pos.Z, pos.Z);
			var projectedCell = map.CellContaining(projectedPos);
			var projectedHeight = pos.Z / 512;

			foreach (var c in map.FindTilesInAnnulus(projectedCell, minRange.Length / 1024, r, true))
			{
				var dist = (map.CenterOfCell(c) - projectedPos).HorizontalLengthSquared;
				if (dist <= maxLimit && (dist == 0 || dist > minLimit))
				{
					var puv = (PPos)c.ToMPos(map);
					if (maxHeightDelta < 0 || map.ProjectedHeight(puv) < projectedHeight + maxHeightDelta)
						yield return puv;
				}
			}
		}

		public static IEnumerable<PPos> ProjectedCellsInRange(Map map, CPos cell, WDist range, int maxHeightDelta = -1)
		{
			return ProjectedCellsInRange(map, map.CenterOfCell(cell), WDist.Zero, range, maxHeightDelta);
		}

		public void AddSource(object key, SourceType type, PPos[] projectedCells)
		{
			if (sources.ContainsKey(key))
				throw new InvalidOperationException("Attempting to add duplicate shroud source");

			sources[key] = new ShroudSource(type, projectedCells);

			foreach (var puv in projectedCells)
			{
				// Force cells outside the visible bounds invisible
				if (!map.Contains(puv))
					continue;

				var uv = (MPos)puv;
				touched[uv] = true;
				switch (type)
				{
					case SourceType.PassiveVisibility:
						passiveVisibilityEnabled = true;
						passiveVisibleCount[uv]++;
						explored[uv] = true;
						break;
					case SourceType.Visibility:
						visibleCount[uv]++;
						explored[uv] = true;
						break;
					case SourceType.Shroud:
						shroudGenerationEnabled = true;
						generatedShroudCount[uv]++;
						break;
				}
			}
		}

		public void RemoveSource(object key)
		{
			ShroudSource state;
			if (!sources.TryGetValue(key, out state))
				return;

			foreach (var puv in state.ProjectedCells)
			{
				// Cells outside the visible bounds don't increment visibleCount
				if (map.Contains(puv))
				{
					var uv = (MPos)puv;
					touched[uv] = true;
					switch (state.Type)
					{
						case SourceType.PassiveVisibility:
							passiveVisibleCount[uv]--;
							break;
						case SourceType.Visibility:
							visibleCount[uv]--;
							break;
						case SourceType.Shroud:
							generatedShroudCount[uv]--;
							break;
					}
				}
			}

			sources.Remove(key);
		}

		public void ExploreProjectedCells(World world, IEnumerable<PPos> cells)
		{
			foreach (var puv in cells)
			{
				var uv = (MPos)puv;
				if (map.Contains(puv) && !explored[uv])
				{
					touched[uv] = true;
					explored[uv] = true;
				}
			}
		}

		public void Explore(Shroud s)
		{
			if (map.Bounds != s.map.Bounds)
				throw new ArgumentException("The map bounds of these shrouds do not match.", "s");

			foreach (var puv in map.ProjectedCellBounds)
			{
				var uv = (MPos)puv;
				if (!explored[uv] && s.explored[uv])
				{
					touched[uv] = true;
					explored[uv] = true;
				}
			}
		}

		public void ExploreAll()
		{
			foreach (var puv in map.ProjectedCellBounds)
			{
				var uv = (MPos)puv;
				if (!explored[uv])
				{
					touched[uv] = true;
					explored[uv] = true;
				}
			}
		}

		public void ResetExploration()
		{
			foreach (var puv in map.ProjectedCellBounds)
			{
				var uv = (MPos)puv;
				touched[uv] = true;
				explored[uv] = (visibleCount[uv] + passiveVisibleCount[uv]) > 0;
			}
		}

		public bool IsExplored(WPos pos)
		{
			return IsExplored(map.ProjectedCellCovering(pos));
		}

		public bool IsExplored(CPos cell)
		{
			return IsExplored(cell.ToMPos(map));
		}

		public bool IsExplored(MPos uv)
		{
			if (!map.Contains(uv))
				return false;

			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsExplored(puv))
					return true;

			return false;
		}

		public bool IsExplored(PPos puv)
		{
			if (Disabled)
				return map.Contains(puv);

			var uv = (MPos)puv;
			return resolvedType.Contains(uv) && resolvedType[uv] > ShroudCellType.Shroud;
		}

		public bool IsVisible(WPos pos)
		{
			return IsVisible(map.ProjectedCellCovering(pos));
		}

		public bool IsVisible(CPos cell)
		{
			return IsVisible(cell.ToMPos(map));
		}

		public bool IsVisible(MPos uv)
		{
			if (!resolvedType.Contains(uv))
				return false;

			foreach (var puv in map.ProjectedCellsCovering(uv))
				if (IsVisible(puv))
					return true;

			return false;
		}

		// In internal shroud coords
		public bool IsVisible(PPos puv)
		{
			if (!FogEnabled)
				return map.Contains(puv);

			var uv = (MPos)puv;
			return resolvedType.Contains(uv) && resolvedType[uv] == ShroudCellType.Visible;
		}

		public bool Contains(PPos uv)
		{
			// Check that uv is inside the map area. There is nothing special
			// about explored here: any of the CellLayers would have been suitable.
			return explored.Contains((MPos)uv);
		}
	}
}
