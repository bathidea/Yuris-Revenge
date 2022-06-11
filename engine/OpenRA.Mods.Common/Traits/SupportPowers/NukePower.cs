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

using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Effects;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	class NukePowerInfo : SupportPowerInfo
	{
		[WeaponReference]
		[FieldLoader.Require]
		[Desc("Weapon to use for the impact.",
			"Also image to use for the missile.")]
		public readonly string MissileWeapon = "";

		[Desc("Delay (in ticks) after launch until the missile is spawned.")]
		public readonly int MissileDelay = 0;

		[SequenceReference("MissileWeapon")]
		[Desc("Sprite sequence for the ascending missile.")]
		public readonly string MissileUp = "up";

		[SequenceReference("MissileWeapon")]
		[Desc("Sprite sequence for the descending missile.")]
		public readonly string MissileDown = "down";

		[Desc("Offset from the actor the missile spawns on.")]
		public readonly WVec SpawnOffset = WVec.Zero;

		[Desc("Altitude offset from the target position at which the warhead should detonate.")]
		public readonly WDist DetonationAltitude = WDist.Zero;

		[Desc("Should nuke missile projectile be removed on detonation above ground.",
			"'False' will make the missile continue until it hits the ground and disappears (without triggering another explosion).")]
		public readonly bool RemoveMissileOnDetonation = true;

		[PaletteReference("IsPlayerPalette")]
		[Desc("Palette to use for the missile weapon image.")]
		public readonly string MissilePalette = "effect";

		[Desc("Custom palette is a player palette BaseName.")]
		public readonly bool IsPlayerPalette = false;

		[Desc("Trail animation.")]
		public readonly string TrailImage = null;

		[SequenceReference("TrailImage")]
		[Desc("Loop a randomly chosen sequence of TrailImage from this list while this projectile is moving.")]
		public readonly string[] TrailSequences = { };

		[Desc("Interval in ticks between each spawned Trail animation.")]
		public readonly int TrailInterval = 1;

		[Desc("Delay in ticks until trail animation is spawned.")]
		public readonly int TrailDelay = 1;

		[PaletteReference("TrailUsePlayerPalette")]
		[Desc("Palette used to render the trail sequence.")]
		public readonly string TrailPalette = "effect";

		[Desc("Use the Player Palette to render the trail sequence.")]
		public readonly bool TrailUsePlayerPalette = false;

		[Desc("Travel time - split equally between ascent and descent.")]
		public readonly int FlightDelay = 400;

		[Desc("Visual ascent velocity in WDist / tick.")]
		public readonly WDist FlightVelocity = new WDist(512);

		[Desc("Descend immediately on the target.")]
		public readonly bool SkipAscent = false;

		[Desc("Amount of time before detonation to remove the beacon.")]
		public readonly int BeaconRemoveAdvance = 25;

		[Desc("Range of cells the camera should reveal around target cell.")]
		public readonly WDist CameraRange = WDist.Zero;

		[Desc("Can the camera reveal shroud generated by the GeneratesShroud trait?")]
		public readonly bool RevealGeneratedShroud = true;

		[Desc("Reveal cells to players with these stances only.")]
		public readonly Stance CameraStances = Stance.Ally;

		[Desc("Amount of time before detonation to spawn the camera.")]
		public readonly int CameraSpawnAdvance = 25;

		[Desc("Amount of time after detonation to remove the camera.")]
		public readonly int CameraRemoveDelay = 25;

		[Desc("Corresponds to `Type` from `FlashPaletteEffect` on the world actor.")]
		public readonly string FlashType = null;

		public WeaponInfo WeaponInfo { get; private set; }

		public override object Create(ActorInitializer init) { return new NukePower(init.Self, this); }
		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!string.IsNullOrEmpty(TrailImage) && !TrailSequences.Any())
				throw new YamlException("At least one entry in TrailSequences must be defined when TrailImage is defined.");

			WeaponInfo weapon;
			var weaponToLower = (MissileWeapon ?? string.Empty).ToLowerInvariant();
			if (!rules.Weapons.TryGetValue(weaponToLower, out weapon))
				throw new YamlException("Weapons Ruleset does not contain an entry '{0}'".F(weaponToLower));

			WeaponInfo = weapon;

			base.RulesetLoaded(rules, ai);
		}
	}

	class NukePower : SupportPower
	{
		readonly NukePowerInfo info;
		BodyOrientation body;

		public NukePower(Actor self, NukePowerInfo info)
			: base(self, info)
		{
			this.info = info;
		}

		protected override void Created(Actor self)
		{
			body = self.TraitOrDefault<BodyOrientation>();
			base.Created(self);
		}

		public override void Activate(Actor self, Order order, SupportPowerManager manager)
		{
			base.Activate(self, order, manager);
			PlayLaunchSounds();

			Activate(self, order.Target.CenterPosition);
		}

		public void Activate(Actor self, WPos targetPosition)
		{
			foreach (var launchpad in self.TraitsImplementing<INotifyNuke>())
				launchpad.Launching(self);

			var palette = info.IsPlayerPalette ? info.MissilePalette + self.Owner.InternalName : info.MissilePalette;
			var skipAscent = info.SkipAscent || body == null;
			var launchPos = skipAscent ? WPos.Zero : self.CenterPosition + body.LocalToWorld(info.SpawnOffset);

			var missile = new NukeLaunch(self.Owner, info.MissileWeapon, info.WeaponInfo, palette, info.MissileUp, info.MissileDown,
				launchPos,
				targetPosition, info.DetonationAltitude, info.RemoveMissileOnDetonation,
				info.FlightVelocity, info.MissileDelay, info.FlightDelay, skipAscent,
				info.FlashType,
				info.TrailImage, info.TrailSequences, info.TrailPalette, info.TrailUsePlayerPalette, info.TrailDelay, info.TrailInterval);

			self.World.AddFrameEndTask(w => w.Add(missile));

			if (info.CameraRange != WDist.Zero)
			{
				var type = info.RevealGeneratedShroud ? Shroud.SourceType.Visibility
					: Shroud.SourceType.PassiveVisibility;

				self.World.AddFrameEndTask(w => w.Add(new RevealShroudEffect(targetPosition, info.CameraRange, type, self.Owner, info.CameraStances,
					info.FlightDelay - info.CameraSpawnAdvance, info.CameraSpawnAdvance + info.CameraRemoveDelay)));
			}

			if (Info.DisplayBeacon)
			{
				var beacon = new Beacon(
					self.Owner,
					targetPosition,
					Info.BeaconPaletteIsPlayerPalette,
					Info.BeaconPalette,
					Info.BeaconImage,
					Info.BeaconPoster,
					Info.BeaconPosterPalette,
					Info.BeaconSequence,
					Info.ArrowSequence,
					Info.CircleSequence,
					Info.ClockSequence,
					() => missile.FractionComplete,
					Info.BeaconDelay,
					info.FlightDelay - info.BeaconRemoveAdvance);

				self.World.AddFrameEndTask(w =>
				{
					w.Add(beacon);
				});
			}
		}
	}
}
