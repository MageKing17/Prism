using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Prism.Injector.Patcher
{
    static class PlayerPatcher
    {
        static CecilContext   context;
        static MemberResolver  memRes;

        static TypeSystem typeSys;
        static TypeDefinition typeDef_Player;

        static void WrapMethods()
        {
            typeDef_Player.GetMethod("GetFileData"    , MethodFlags.Public | MethodFlags.Static, typeSys.String, typeSys.Boolean).Wrap(context);
            typeDef_Player.GetMethod("ItemCheck"      , MethodFlags.Public | MethodFlags.Instance                               ).Wrap(context);
            typeDef_Player.GetMethod("KillMe"         , MethodFlags.Public | MethodFlags.Instance                               ).Wrap(context);
            typeDef_Player.GetMethod("Update"         , MethodFlags.Public | MethodFlags.Instance, typeSys.Int32                ).Wrap(context);
            typeDef_Player.GetMethod("UpdateEquips"   , MethodFlags.Public | MethodFlags.Instance, typeSys.Int32                ).Wrap(context);
            typeDef_Player.GetMethod("UpdateArmorSets", MethodFlags.Public | MethodFlags.Instance, typeSys.Int32                ).Wrap(context);
            typeDef_Player.GetMethod("WingMovement"   , MethodFlags.Public | MethodFlags.Instance                               ).Wrap(context);
            typeDef_Player.GetMethod("UpdateBuffs"    , MethodFlags.Public | MethodFlags.Instance, typeSys.Int32                ).Wrap(context);
            typeDef_Player.GetMethod("AddBuff"        , MethodFlags.Public | MethodFlags.Instance                               ).Wrap(context);

            var typeDef_uiCharSelect = memRes.GetType("Terraria.GameContent.UI.States.UICharacterSelect");

            typeDef_uiCharSelect.GetMethod("NewCharacterClick").Wrap(context);
        }

        static void AddFieldForBHandler()
        {
            typeDef_Player.Fields.Add(new FieldDefinition("P_BHandler"    , FieldAttributes.Public, typeSys.Object));
            typeDef_Player.Fields.Add(new FieldDefinition("P_BuffBHandler", FieldAttributes.Public, memRes.ReferenceOf(typeof(object[]))));
        }

        static void InsertSaveLoadHooks()
        {
            Instruction[] toInject;
            MethodDefinition method;

            // Insert save hook at end of SavePlayer
            TypeDefinition typeDef_PlayerFileData = memRes.GetType("Terraria.IO.PlayerFileData");

            toInject = new Instruction[]
            {
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Call, new MethodReference("Prism.Mods.SaveDataHandler.SavePlayer", typeSys.Void)) // Obviously doesnt work
            };
            method = typeDef_Player.GetMethod("SavePlayer", MethodFlags.Public | MethodFlags.Static, typeDef_PlayerFileData, typeSys.Boolean);
            ILInjector.Inject(new InjectionData[] { InjectionData.Method.NewMethodPost(method, toInject) });

            // Insert load hook near end of LoadPlayer
            toInject = new Instruction[]
            {
                Instruction.Create(OpCodes.Ldloc_1),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Call, new MethodReference("Prism.Mods.SaveDataHandler.LoadPlayer", typeSys.Void)) // Obviously doesn't work
            };
            method = typeDef_Player.GetMethod("LoadPlayer", MethodFlags.Public | MethodFlags.Static, typeSys.String, typeSys.Boolean);

            OpCode[] toFind =
            {
                // player.skinVariant = (int)MathHelper.Clamp((float)player.skinVariant, 0f, 7f);
                OpCodes.Ldloc_1,    //ldloc.1
                OpCodes.Ldloc_1,    //ldloc.1
                OpCodes.Ldfld,      //ldfld int32 Terraria.Player::skinVariant
                OpCodes.Conv_R4,    //conv.r4
                OpCodes.Ldc_R4,     //ldc.r4 0.0
                OpCodes.Ldc_R4,     //ldc.r4 7
                OpCodes.Call,       //call float32[Microsoft.Xna.Framework]Microsoft.Xna.Framework.MathHelper::Clamp(float32, float32, float32)
                OpCodes.Conv_I4,    //conv.i4
                OpCodes.Stfld       //stfld int32 Terraria.Player::skinVariant
            };

            MethodBody lpb = method.Body;
            ILProcessor ilp = lpb.GetILProcessor();
            Instruction first = lpb.FindInstrSeqStart(toFind);

            foreach (Instruction instruction in toInject)
                ilp.InsertBefore(first, instruction);
        }

        /// <summary>
        /// Removes the ID checks from player loading, so that invalid items
        /// are removed instead of resulting in the character being declared
        /// invalid. If this gets fixed in the original, this code should be
        /// removed.
        /// </summary>
        static void RemoveBuggyPlayerLoading()
        {
            OpCode[] seqToRemove =
            {
                OpCodes.Ldloc_S,
                OpCodes.Ldc_I4,
                OpCodes.Blt_S,
                TerrariaPatcher.Platform == Platform.Windows ? OpCodes.Ldloc_1 : OpCodes.Ldloc_2,
                OpCodes.Ldfld,
                OpCodes.Ldloc_S,
                OpCodes.Ldelem_Ref,
                OpCodes.Ldc_I4_0,
                OpCodes.Callvirt,
                OpCodes.Br_S,
            };

            var loadPlayerBody = typeDef_Player.GetMethod("LoadPlayer", MethodFlags.Public | MethodFlags.Static, typeSys.String, typeSys.Boolean).Body;
            var processor = loadPlayerBody.GetILProcessor();
            int count = 0;

            while (true)
            {
                var firstInstr = loadPlayerBody.FindInstrSeqStart(seqToRemove);
                if (firstInstr != null)
                {
                    count++;
                    processor.RemoveInstructions(firstInstr, seqToRemove.Length);
                }
                else
                {
                    if (count == 0)
                    {
                        Console.WriteLine("PlayerPatcher.RemoveBuggyPlayerLoading() could not find the target instruction sequence; Terraria.Player.LoadPlayer() may have been fixed, and this hack can be removed.");
                    }
                    else if (count != 6)
                    {
                        Console.WriteLine("PlayerPatcher.RemoveBuggyPlayerLoading() removed " + count.ToString() + " instances of the target instruction sequence instead of 6; Terraria.Player.LoadPlayer() logic may have changed, and this hack may be superflous/harmful!");
                    }
                    break;
                }
            }
        }

        static void ReplaceUseSoundCalls()
        {
            var typeDef_Item = memRes.GetType("Terraria.Item");

            #region ItemCheck
            {
                var itemCheck = typeDef_Player.GetMethod("RealItemCheck"); // this method is wrapped

                MethodDefinition invokeUseSound;
                var onItemCheckUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_ItemCheck_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var itemCheck_PlayUseSound0 = new FieldDefinition("P_ItemCheck_PlayUseSound0", FieldAttributes.Public | FieldAttributes.Static, onItemCheckUseSound);
                typeDef_Player.Fields.Add(itemCheck_PlayUseSound0);

                OpCode[] toRem =
                {
                    OpCodes.Ldloc_2,
                    OpCodes.Ldfld, // Item.sound
                    OpCodes.Ldc_I4_0,
                    OpCodes.Ble_S, // <after the call>

                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldloc_2,
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var icproc = itemCheck.Body.GetILProcessor();

                var first = itemCheck.Body.FindInstrSeqStart(toRem);
                first = icproc.RemoveInstructions(first, toRem.Length);

                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, itemCheck_PlayUseSound0));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_2));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));

                // ---

                var itemCheck_PlayUseSound1 = new FieldDefinition("P_ItemCheck_PlayUseSound1", FieldAttributes.Public | FieldAttributes.Static, onItemCheckUseSound);
                typeDef_Player.Fields.Add(itemCheck_PlayUseSound1);

                toRem = new[]
                {
                    OpCodes.Ldloc_2,
                    OpCodes.Ldfld, // Item.sound
                    OpCodes.Ldc_I4_0,
                    OpCodes.Ble_S, // <after the call>

                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldloc_2,
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                first = itemCheck.Body.FindInstrSeqStart(toRem);
                first = icproc.RemoveInstructions(first, toRem.Length);

                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, itemCheck_PlayUseSound1));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_2));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                icproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region QuickBuff
            {
                var quickBuff = typeDef_Player.GetMethod("QuickBuff");

                MethodDefinition invokeUseSound;
                var onQuickBuffUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_QuickBuff_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var quickBuff_PlayUseSound = new FieldDefinition("P_QuickBuff_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onQuickBuffUseSound);
                typeDef_Player.Fields.Add(quickBuff_PlayUseSound);

                var qbb = quickBuff.Body;
                var qbproc = qbb.GetILProcessor();

                // change local 0 to an item (instead of item.useSound int)
                qbb.Variables[0].VariableType = typeDef_Item;

                // windows build uses short form -> addresses get messed up, but they're too specific to use anything else

                // remove .useSound
                var inst = qbb.Instructions.First(i => i.Offset == (TerrariaPatcher.Platform == Platform.Windows ? 0x0247 : 0x02aa));
                qbproc.Remove(inst);

                // change ldc.i4.0 to ldnull
                inst = qbb.Instructions.First(i => i.Offset == (TerrariaPatcher.Platform == Platform.Windows ? 0x02d2 : 0x033e));
                var p = inst.Previous;
                qbproc.Remove(inst);
                qbproc.InsertAfter(p, Instruction.Create(OpCodes.Ldnull));

                // change ble(.s) to beq(.s)
                inst = qbb.Instructions.First(i => i.Offset == (TerrariaPatcher.Platform == Platform.Windows ? 0x02d3 : 0x033f));
                p = inst.Previous;
                qbproc.Remove(inst);
                qbproc.InsertAfter(p, Instruction.Create(TerrariaPatcher.Platform == Platform.Windows ? OpCodes.Beq_S : OpCodes.Beq, (Instruction)inst.Operand));

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldloc_0,
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var first = qbb.FindInstrSeqStart(toRem);
                first = qbproc.RemoveInstructions(first, toRem.Length);

                qbproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, quickBuff_PlayUseSound));
                qbproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_0)); // load item instance ons stack
                qbproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                qbproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region QuickGrapple
            {
                var quickGrapple = typeDef_Player.GetMethod("QuickGrapple");

                MethodDefinition invokeUseSound;
                var onQuickGrappleUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_QuickGrapple_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var quickGrapple_PlayUseSound = new FieldDefinition("P_QuickGrapple_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onQuickGrappleUseSound);
                typeDef_Player.Fields.Add(quickGrapple_PlayUseSound);

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldloc_0,
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var qgproc = quickGrapple.Body.GetILProcessor();

                var first = quickGrapple.Body.FindInstrSeqStart(toRem);
                first = qgproc.RemoveInstructions(first, toRem.Length);

                qgproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, quickGrapple_PlayUseSound));
                qgproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_0)); // load item instance ons stack
                qgproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                qgproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region QuickHeal
            {
                var quickHeal = typeDef_Player.GetMethod("QuickHeal");

                MethodDefinition invokeUseSound;
                var onQuickHealUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_QuickHeal_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var quickHeal_PlayUseSound = new FieldDefinition("P_QuickHeal_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onQuickHealUseSound);
                typeDef_Player.Fields.Add(quickHeal_PlayUseSound);

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldloc_1,
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var qhproc = quickHeal.Body.GetILProcessor();

                var first = quickHeal.Body.FindInstrSeqStart(toRem);
                first = qhproc.RemoveInstructions(first, toRem.Length);

                qhproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, quickHeal_PlayUseSound));
                qhproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_1)); // load item instance ons stack
                qhproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                qhproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region QuickMana
            {
                var quickMana = typeDef_Player.GetMethod("QuickMana");

                MethodDefinition invokeUseSound;
                var onQuickManaUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_QuickMana_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var quickMana_PlayUseSound = new FieldDefinition("P_QuickMana_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onQuickManaUseSound);
                typeDef_Player.Fields.Add(quickMana_PlayUseSound);

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldfld, // Player.inventory
                    OpCodes.Ldloc_0,
                    OpCodes.Ldelem_Ref, // <top of stack>[i]
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var qmproc = quickMana.Body.GetILProcessor();

                var first = quickMana.Body.FindInstrSeqStart(toRem);
                first = qmproc.RemoveInstructions(first, toRem.Length);

                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, quickMana_PlayUseSound));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, typeDef_Player.GetField("inventory")));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldloc_0));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldelem_Ref));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                qmproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region QuickMount
            {
                var quickMount = typeDef_Player.GetMethod("QuickMount");

                MethodDefinition invokeUseSound;
                var onQuickMountUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_QuickMount_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var quickMount_PlayUseSound = new FieldDefinition("P_QuickMount_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onQuickMountUseSound);
                typeDef_Player.Fields.Add(quickMount_PlayUseSound);

                OpCode[] toRem = TerrariaPatcher.Platform == Platform.Windows
                    ? new[]
                    {
                        OpCodes.Ldloc_0,
                        OpCodes.Ldfld, // Item.sound
                        OpCodes.Ldc_I4_0,
                        OpCodes.Ble_S, // <after the call>

                        OpCodes.Ldc_I4_2, // UseItem ID
                        OpCodes.Ldarg_0,
                        OpCodes.Call, // Entity.Center
                        OpCodes.Ldfld, // Vector2.X
                        OpCodes.Conv_I4,
                        OpCodes.Ldarg_0,
                        OpCodes.Call, // Entity.Center
                        OpCodes.Ldfld, // Vector2.Y
                        OpCodes.Conv_I4,
                        OpCodes.Ldloc_0,
                        OpCodes.Ldfld, // Item.useSound
                        OpCodes.Call // Main.PlaySound(int, int, int, int)
                    }
                    : new[]
                    {
                        OpCodes.Ldloc_0,
                        OpCodes.Ldfld, // Item.sound
                        OpCodes.Ldc_I4_0,
                        OpCodes.Ble_S, // <after the call>

                        OpCodes.Ldc_I4_2, // UseItem ID
                        OpCodes.Ldarg_0,
                        OpCodes.Call, // Entity.Center
                        OpCodes.Stloc_2,
                        OpCodes.Ldloca_S,
                        OpCodes.Ldfld, // Vector2.X
                        OpCodes.Conv_I4,
                        OpCodes.Ldarg_0,
                        OpCodes.Call, // Entity.Center
                        OpCodes.Stloc_2,
                        OpCodes.Ldloca_S,
                        OpCodes.Ldfld, // Vector2.Y
                        OpCodes.Conv_I4,
                        OpCodes.Ldloc_0,
                        OpCodes.Ldfld, // Item.useSound
                        OpCodes.Call // Main.PlaySound(int, int, int, int)
                    };

                var qmproc = quickMount.Body.GetILProcessor();

                var first = quickMount.Body.FindInstrSeqStart(toRem);
                var index = quickMount.Body.Instructions.IndexOf(first);
                var next = quickMount.Body.Instructions[index + toRem.Length];

                first = qmproc.RemoveInstructions(first, toRem.Length);

                qmproc.InsertBefore(next, Instruction.Create(OpCodes.Ldsfld, quickMount_PlayUseSound));
                qmproc.InsertBefore(next, Instruction.Create(OpCodes.Ldloc_0));
                qmproc.InsertBefore(next, Instruction.Create(OpCodes.Ldarg_0));
                qmproc.InsertBefore(next, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region UpdatePet
            {
                var updatePet = typeDef_Player.GetMethod("UpdatePet");

                MethodDefinition invokeUseSound;
                var onUpdatePetUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_UpdatePet_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var updatePet_PlayUseSound = new FieldDefinition("P_UpdatePet_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onUpdatePetUseSound);
                typeDef_Player.Fields.Add(updatePet_PlayUseSound);

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldfld, // Player.miscEquips
                    OpCodes.Ldc_I4_0,
                    OpCodes.Ldelem_Ref, // <top of stack>[0]
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var upproc = updatePet.Body.GetILProcessor();

                var first = updatePet.Body.FindInstrSeqStart(toRem);
                first = upproc.RemoveInstructions(first, toRem.Length);

                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, updatePet_PlayUseSound));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, typeDef_Player.GetField("miscEquips")));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_0));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldelem_Ref));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                upproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion

            #region UpdatePetLight
            {
                var updatePetLight = typeDef_Player.GetMethod("UpdatePetLight");

                MethodDefinition invokeUseSound;
                var onUpdatePetLightUseSound = context.CreateDelegate("Terraria.PrismInjections", "Player_UpdatePetLight_PlayUseSoundDel", typeSys.Void, out invokeUseSound, typeDef_Item, typeDef_Player);

                var updatePetLight_PlayUseSound = new FieldDefinition("P_UpdatePetLight_PlayUseSound", FieldAttributes.Public | FieldAttributes.Static, onUpdatePetLightUseSound);
                typeDef_Player.Fields.Add(updatePetLight_PlayUseSound);

                OpCode[] toRem =
                {
                    OpCodes.Ldc_I4_2, // UseItem ID
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.X
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldflda, // Entity.position
                    OpCodes.Ldfld, // Vector2.Y
                    OpCodes.Conv_I4,
                    OpCodes.Ldarg_0,
                    OpCodes.Ldfld, // Player.miscEquips
                    OpCodes.Ldc_I4_1,
                    OpCodes.Ldelem_Ref, // <top of stack>[1]
                    OpCodes.Ldfld, // Item.useSound
                    OpCodes.Call // Main.PlaySound(int, int, int, int)
                };

                var uplproc = updatePetLight.Body.GetILProcessor();

                var first = updatePetLight.Body.FindInstrSeqStart(toRem);
                first = uplproc.RemoveInstructions(first, toRem.Length);

                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, updatePetLight_PlayUseSound));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldfld, typeDef_Player.GetField("miscEquips")));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I4_1));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldelem_Ref));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                uplproc.InsertBefore(first, Instruction.Create(OpCodes.Call, invokeUseSound));
            }
            #endregion
        }

        static void FixOnEnterWorldField()
        {
            // wtf?
            typeDef_Player.GetField("OnEnterWorld").Name = "_onEnterWorld_backingField";
        }

        static void InjectMidUpdate()
        {
            var update = typeDef_Player.GetMethod("RealUpdate" /* method is wrapped */, MethodFlags.Public | MethodFlags.Instance, typeSys.Int32);
            var grabItems = typeDef_Player.GetMethod("GrabItems", MethodFlags.Public | MethodFlags.Instance, typeSys.Int32);

            MethodDefinition invokeMidUpdate;
            var onMidUpdateDel = context.CreateDelegate("Terraria.PrismInjections", "Player_MidUpdateDel", typeSys.Void, out invokeMidUpdate, typeDef_Player, typeSys.Int32);

            var onMidUpdate = new FieldDefinition("P_OnMidUpdate", FieldAttributes.Public | FieldAttributes.Static, onMidUpdateDel);
            typeDef_Player.Fields.Add(onMidUpdate);

            var ub = update.Body;
            var uproc = ub.GetILProcessor();

            OpCode[] callGrabItems =
            {
                OpCodes.Ldarg_0, // this
                OpCodes.Ldarg_1, // id
                OpCodes.Call, // Player.GrabItems

                OpCodes.Ldsfld, // Main.mapFullScreen
                OpCodes.Brtrue
            };

            Instruction instrs;

            int startInd = 0;
            while (true)
            {
                instrs = ub.FindInstrSeqStart(callGrabItems, startInd);

                if (instrs == null)
                    break;

                startInd = ub.Instructions.IndexOf(instrs) + 3;

                var mtdToCall = instrs.Next.Next.Operand as MethodReference;

                if (mtdToCall == null)
                    continue;

                // close enough
                if (mtdToCall.DeclaringType == grabItems.DeclaringType && mtdToCall.FullName == grabItems.FullName)
                    break;
            }

            instrs = instrs.Next.Next.Next;

            uproc.InsertBefore(instrs, Instruction.Create(OpCodes.Ldsfld, onMidUpdate));
            uproc.EmitWrapperCall(invokeMidUpdate, instrs);
        }

        static void InitBuffBHandlerArray()
        {
            var ctor = typeDef_Player.GetMethod(".ctor");
            var buffBHandler = typeDef_Player.GetField("P_BuffBHandler");

            var cproc = ctor.Body.GetILProcessor();

            var l = ctor.Body.Instructions.Last().Previous.Previous;

            cproc.InsertBefore(l, Instruction.Create(OpCodes.Ldarg_0));
            cproc.InsertBefore(l, Instruction.Create(OpCodes.Ldc_I4, 22));
            cproc.InsertBefore(l, Instruction.Create(OpCodes.Newarr, typeSys.Object));
            cproc.InsertBefore(l, Instruction.Create(OpCodes.Stfld, buffBHandler));
        }

        internal static void Patch()
        {
            context = TerrariaPatcher.context;
            memRes  = TerrariaPatcher.memRes;

            typeSys = context.PrimaryAssembly.MainModule.TypeSystem;
            typeDef_Player = memRes.GetType("Terraria.Player");

            WrapMethods();
            AddFieldForBHandler();
            //InsertSaveLoadHooks();
            RemoveBuggyPlayerLoading();
            ReplaceUseSoundCalls();
            FixOnEnterWorldField();
            InjectMidUpdate();
            InitBuffBHandlerArray();
        }
    }
}
