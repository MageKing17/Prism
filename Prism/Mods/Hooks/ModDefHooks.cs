﻿using System;
using System.Collections.Generic;
using System.Linq;
using Prism.API;
using Prism.API.Audio;
using Terraria;

namespace Prism.Mods.Hooks
{
    class ModDefHooks : IHookManager
    {
        IEnumerable<Action>
            onAllModsLoaded,
            onUnload       ;

        public void Create()
        {
            onAllModsLoaded = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "OnAllModsLoaded");
            onUnload        = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "OnUnload"       );
        }
        public void Clear ()
        {
            onAllModsLoaded = null;
            onUnload        = null;
        }

        public void OnAllModsLoaded()
        {
            HookManager.Call(onAllModsLoaded);
        }
        public void OnUnload()
        {
            HookManager.Call(onUnload);
        }
    }
}
