﻿using System;
using System.Collections.Generic;
using System.Linq;
using Prism.API.Defs;
using Terraria;
using Terraria.ID;

namespace Prism.Mods.DefHandlers
{
    sealed class WallDefHandler : EntityDefHandler<WallDef, int>
    {
        internal readonly static string WALL = "Wall";

        protected override Type IDContainerType
        {
            get
            {
                return typeof(WallID);
            }
        }

        protected override void ExtendVanillaArrays(int amt = 1)
        {
            if (amt == 0)
                return;

            int newLen = amt > 0 ? Main.wallDungeon.Length + amt : TileID.Count;

            if (!Main.dedServ)
                Array.Resize(ref Main.wallTexture, newLen);

            Array.Resize(ref Main.wallLoaded , newLen);
            Array.Resize(ref Main.wallDungeon, newLen);
            Array.Resize(ref Main.wallHouse  , newLen);
            Array.Resize(ref Main.wallLight  , newLen);

            Array.Resize(ref WallID.Sets.Corrupt    , newLen);
            Array.Resize(ref WallID.Sets.Crimson    , newLen);
            Array.Resize(ref WallID.Sets.Hallow     , newLen);
            Array.Resize(ref WallID.Sets.Transparent, newLen);

            Array.Resize(ref WallID.Sets.Conversion.Grass       , newLen);
            Array.Resize(ref WallID.Sets.Conversion.HardenedSand, newLen);
            Array.Resize(ref WallID.Sets.Conversion.Sandstone   , newLen);
            Array.Resize(ref WallID.Sets.Conversion.Stone       , newLen);
        }

        protected override int GetVanillaEntityFromID(int id)
        {
            return id;
        }
        protected override WallDef NewDefFromVanilla(int id)
        {
            return new WallDef(String.Empty, () => Main.wallTexture[id])
            {
                Type = id
            };
        }

        protected override void CopyEntityToDef(int id, WallDef wall)
        {
            wall.DisplayName = wall.InternalName ?? (WALL + id);

            wall.ConversionData = new WallConversionData(
                WallID.Sets.Conversion.Grass       [id],
                WallID.Sets.Conversion.HardenedSand[id],
                WallID.Sets.Conversion.Sandstone   [id],
                WallID.Sets.Conversion.Stone       [id]
                );

            wall.IsCorruption  = WallID.Sets.Corrupt    [id];
            wall.IsCrimson     = WallID.Sets.Crimson    [id];
            wall.IsHallow      = WallID.Sets.Hallow     [id];
            wall.IsTransparent = WallID.Sets.Transparent[id];

            wall.IsDungeonWall        = Main.wallDungeon[id];
            wall.IsSuitableForHousing = Main.wallHouse  [id];
            wall.Light                = Main.wallLight  [id];
        }
        protected override void CopyDefToEntity(WallDef wall, int id)
        {
            // ...
        }

        protected override List<LoaderError> CheckTextures(WallDef def)
        {
            var ret = new List<LoaderError>();

            if (def.GetTexture == null)
                ret.Add(new LoaderError(def.Mod, "GetTexture of WallDef " + def + " is null."));

            return ret;
        }
        protected override List<LoaderError> LoadTextures (WallDef def)
        {
            var ret = new List<LoaderError>();

            var t = def.GetTexture();
            if (t == null)
            {
                ret.Add(new LoaderError(def.Mod, "GetTexture return value is null for WallDef " + def + "."));
                return ret;
            }

            Main.wallTexture[def.Type] = t;
            Main.wallLoaded [def.Type] = true;

            return ret;
        }

        protected override int GetRegularType(int id)
        {
            return id;
        }

        protected override void CopySetProperties(WallDef def)
        {
            Main.wallDungeon[def.Type] = def.IsDungeonWall       ;
            Main.wallHouse  [def.Type] = def.IsSuitableForHousing;
            Main.wallLight  [def.Type] = def.Light               ;

            WallID.Sets.Corrupt    [def.Type] = def.IsCorruption ;
            WallID.Sets.Crimson    [def.Type] = def.IsCrimson    ;
            WallID.Sets.Hallow     [def.Type] = def.IsHallow     ;
            WallID.Sets.Transparent[def.Type] = def.IsTransparent;

            WallID.Sets.Conversion.Grass       [def.Type] = def.ConversionData.IsGrass       ;
            WallID.Sets.Conversion.HardenedSand[def.Type] = def.ConversionData.IsHardenedSand;
            WallID.Sets.Conversion.Sandstone   [def.Type] = def.ConversionData.IsSandstone   ;
            WallID.Sets.Conversion.Stone       [def.Type] = def.ConversionData.IsStone       ;
        }
    }
}
