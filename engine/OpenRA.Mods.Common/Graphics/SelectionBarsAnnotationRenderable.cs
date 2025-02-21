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

using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Graphics
{
	public struct SelectionBarsAnnotationRenderable : IRenderable, IFinalizedRenderable
	{
		readonly WPos pos;
		readonly Actor actor;
		readonly bool displayHealth;
		readonly bool displayExtra;
		readonly Rectangle decorationBounds;

		public SelectionBarsAnnotationRenderable(Actor actor, Rectangle decorationBounds, bool displayHealth, bool displayExtra)
			: this(actor.CenterPosition, actor, decorationBounds)
		{
			this.displayHealth = displayHealth;
			this.displayExtra = displayExtra;
		}

		public SelectionBarsAnnotationRenderable(WPos pos, Actor actor, Rectangle decorationBounds)
			: this()
		{
			this.pos = pos;
			this.actor = actor;
			this.decorationBounds = decorationBounds;
		}

		public WPos Pos { get { return pos; } }
		public bool DisplayHealth { get { return displayHealth; } }
		public bool DisplayExtra { get { return displayExtra; } }

		public PaletteReference Palette { get { return null; } }
		public int ZOffset { get { return 0; } }
		public bool IsDecoration { get { return true; } }

		public IRenderable WithPalette(PaletteReference newPalette) { return this; }
		public IRenderable WithZOffset(int newOffset) { return this; }
		public IRenderable OffsetBy(WVec vec) { return new SelectionBarsAnnotationRenderable(pos + vec, actor, decorationBounds); }
		public IRenderable AsDecoration() { return this; }

		void DrawExtraBars(WorldRenderer wr, float2 start, float2 end)
		{
			foreach (var extraBar in actor.TraitsImplementing<ISelectionBar>())
			{
				var value = extraBar.GetValue();
				if (value != 0 || extraBar.DisplayWhenEmpty)
				{
					var offset = new float2(0, 4);
					start += offset;
					end += offset;
					DrawSelectionBar(wr, start, end, extraBar.GetValue(), extraBar.GetColor());
				}
			}
		}

		void DrawSelectionBar(WorldRenderer wr, float2 start, float2 end, float value, Color barColor)
		{
			var c = Color.FromArgb(128, 30, 30, 30);
			var c2 = Color.FromArgb(128, 10, 10, 10);
			var p = new float2(0, -4);
			var q = new float2(0, -3);
			var r = new float2(0, -2);

			var barColor2 = Color.FromArgb(255, barColor.R / 2, barColor.G / 2, barColor.B / 2);

			var z = float3.Lerp(start, end, value);
			var cr = Game.Renderer.RgbaColorRenderer;
			cr.DrawLine(start + p, end + p, 1, c);
			cr.DrawLine(start + q, end + q, 1, c2);
			cr.DrawLine(start + r, end + r, 1, c);

			cr.DrawLine(start + p, z + p, 1, barColor2);
			cr.DrawLine(start + q, z + q, 1, barColor);
			cr.DrawLine(start + r, z + r, 1, barColor2);
		}

		Color GetHealthColor(IHealth health)
		{
			if (Game.Settings.Game.UsePlayerStanceColors)
				return actor.Owner.PlayerStanceColor(actor);

			return health.DamageState == DamageState.Critical ? Color.Red :
				health.DamageState == DamageState.Heavy ? Color.Yellow : Color.LimeGreen;
		}

		System.Tuple<Color, Color, Color> GetHealthColorSet(Color healthColorBase)
		{
			if (healthColorBase == Color.Red)
			{
					return new System.Tuple<Color, Color, Color>(
						Color.FromArgb(100, 0, 0),
						Color.FromArgb(253, 0, 0),
						Color.FromArgb(253, 91, 0));
			}
			else if (healthColorBase == Color.Yellow)
			{
				return new System.Tuple<Color, Color, Color>(
						Color.FromArgb(253, 135, 0),
						Color.FromArgb(253, 210, 0),
						Color.FromArgb(253, 253, 0));
			} // if (healthColorBase == Color.Green)
			else
			{
				return new System.Tuple<Color, Color, Color>(
						Color.FromArgb(0, 86, 0),
						Color.FromArgb(0, 196, 0),
						Color.FromArgb(0, 253, 0));
			}
		}

		void DrawHealthBar(WorldRenderer wr, IHealth health, float2 start, float2 end)
		{
			if (health == null || health.IsDead)
				return;

			// red alert 2 boundary box color is white
			var border_color = Color.FromArgb(255, 255, 255, 255);
			var hpBgColor1 = Color.FromArgb(200, 22, 22, 22);
			var hpBgColor2 = Color.FromArgb(200, 0, 0, 0);
			var hpOffset1 = new float2(0, -4);
			var hpOffset2 = new float2(0, -3);
			var hpOffset3 = new float2(0, -2);

			var stbox = new float2(-1, -1);
			var enbox = new float2(0, 2);
			var minusY1 = new float2(0, -1);
			var plusY1 = new float2(0, 1);
			var healthColor = GetHealthColor(health);

			var hpColorSet = GetHealthColorSet(healthColor);

			var z = float3.Lerp(start, end, (float)health.HP / health.MaxHP);

			// bar boundary
			var cr = Game.Renderer.RgbaColorRenderer;

			// cr.DrawLine(start + p + minus, end + p + minus, 1, c);
			cr.DrawRect(start + hpOffset1 + stbox, end + hpOffset1 + enbox, 1, border_color);

			// cr.DrawLine(start + q + minus, end + q + plus, 1, c2);
			// cr.DrawLine(start + r + minus, end + r + plus, 1, c);

			// draw background
			var xi = new float2(start.X, start.Y + hpOffset1.Y);
			while (xi.X < end.X)
			{
				var stXi = new float2(xi.X, xi.Y);
				var endXi = new float2(xi.X, xi.Y + 2);
				cr.DrawLine(stXi, endXi, 1, hpBgColor1);
				if (xi.X + 1 >= end.X)
					break;
				stXi = new float2(xi.X + 1, xi.Y);
				endXi = new float2(xi.X + 1, xi.Y + 2);
				cr.DrawLine(stXi, endXi, 1, hpBgColor2);
				xi = new float2(xi.X + 2, xi.Y);
			}

			// real color bar
			// something strange , width line size 2,
			// it start draw from bottom to top
			// so we need to plus y 1
			cr.DrawLine(start + hpOffset1, z + hpOffset1, 1, hpColorSet.Item3);
			cr.DrawLine(start + hpOffset2, z + hpOffset2, 1, hpColorSet.Item2);

			// draw sub line
			xi = new float2(start.X, start.Y + hpOffset1.Y);
			while (xi.X < z.X)
			{
				var stXi = new float2(xi.X, xi.Y);
				var endXi = new float2(xi.X, xi.Y + 2);
				cr.DrawLine(stXi, endXi, 1, hpColorSet.Item1);
				xi = new float2(xi.X + 2, xi.Y);
				if (xi.X + 1 >= end.X)
					break;
			}

			/*
			if (health.DisplayHP != health.HP)
			{
				var deltaColor = Color.OrangeRed;
				var deltaColor2 = Color.FromArgb(
					255,
					deltaColor.R / 2,
					deltaColor.G / 2,
					deltaColor.B / 2);
				var zz = float3.Lerp(start, end, (float)health.DisplayHP / health.MaxHP);

				cr.DrawLine(z + p, zz + p, 1, deltaColor2);
				cr.DrawLine(z + q, zz + q, 1, deltaColor);
				cr.DrawLine(z + r, zz + r, 1, deltaColor2);
			}
			*/
		}

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }
		public void Render(WorldRenderer wr)
		{
			if (!actor.IsInWorld || actor.IsDead)
				return;

			var health = actor.TraitOrDefault<IHealth>();
			var start = wr.Viewport.WorldToViewPx(new float2(decorationBounds.Left + 1, decorationBounds.Top));
			var end = wr.Viewport.WorldToViewPx(new float2(decorationBounds.Right - 1, decorationBounds.Top));

			int2 minusSt = new int2(9, 0);
			int2 minusEd = new int2(-9, 0);
			start = start + minusSt;
			end = end + minusEd;

			if (DisplayHealth)
				DrawHealthBar(wr, health, start, end);

			if (DisplayExtra)
				DrawExtraBars(wr, start, end);
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }
	}
}
