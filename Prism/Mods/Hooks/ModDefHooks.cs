﻿using System;
using System.Collections.Generic;
using System.Linq;
using Prism.API;

namespace Prism.Mods.Hooks
{
    class ModDefHooks : IHookManager
    {
        IEnumerable<Action>
            onAllModsLoaded,
            onUnload       ,
            preUpdate      ,
            postUpdate     ;

        public void Create()
        {
            onAllModsLoaded = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "OnAllModsLoaded");
            onUnload        = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "OnUnload"       );
            preUpdate       = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "PreUpdate"      );
            postUpdate      = HookManager.CreateHooks<ModDef, Action>(ModData.mods.Values, "PostUpdate"     );
        }
        public void Clear ()
        {
            onAllModsLoaded = null;
            onUnload        = null;
            postUpdate      = null;
        }

        public void OnAllModsLoaded()
        {
            HookManager.Call(onAllModsLoaded);
        }
        public void OnUnload       ()
        {
            HookManager.Call(onUnload);
        }
        public void PreUpdate     ()
        {
            HookManager.Call(preUpdate);
        }
        public void PostUpdate     ()
        {
            HookManager.Call(postUpdate);
        }
    }
}
