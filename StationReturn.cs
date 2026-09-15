using System;
using HarmonyLib;
using UnityEngine;
using Mod = StationItemReturn.StationItemReturn;

namespace StationItemReturn
{
    internal static class StationReturn
    {
        private const string ReturnCookingItemRpc = "RPC_SIR_ReturnCookingItem";
        private const string ReturnCookingFuelRpc = "RPC_SIR_ReturnCookingFuel";
        private const string RefreshCookingStationRpc = "RPC_SIR_RefreshCookingStation";
        private const string ReturnFireplaceFuelRpc = "RPC_SIR_ReturnFireplaceFuel";
        private const string RefreshFireplaceRpc = "RPC_SIR_RefreshFireplace";
        private const string RecordCookingSkillRpc = "RPC_SIR_RecordCookingSkill";
        private const string ReturnSmelterInputRpc = "RPC_SIR_ReturnSmelterInput";
        private const string ReturnSmelterFuelRpc = "RPC_SIR_ReturnSmelterFuel";
        private const string RefreshSmelterRpc = "RPC_SIR_RefreshSmelter";
        private const string ReturnFermenterIngredientRpc = "RPC_SIR_ReturnFermenterIngredient";
        private const string RefreshFermenterRpc = "RPC_SIR_RefreshFermenter";
        private const string ReturnTurretAmmoRpc = "RPC_SIR_ReturnTurretAmmo";
        private const string RefreshTurretRpc = "RPC_SIR_RefreshTurret";
        private const string UndoSkillRpc = "RPC_SIR_UndoStationSkill";

        private const string LastCookingAuthorKey = "SIR_LastCookingAuthor";
        private const string LastCookingSlotKey = "SIR_LastCookingSlot";

        private static long activeCookingAuthor;
        private static int blockedHoldSourceId;
        private static ZRoutedRpc registeredRoutedRpc;

        private struct CookingInteractionState
        {
            internal bool HadDoneItem;
            internal int FreeSlot;
            internal string ExpectedPrefabName;
        }

        private static string CookingAuthorKey(int slot) => "SIR_CookingAuthor_" + slot;
        private static string CookingSkillKey(int slot) => "SIR_CookingSkill_" + slot;

        internal static bool IsEnabled => Mod.ModEnabled?.Value == true && Mod.IsAvailableForCurrentSession;

        private static bool IsValidOwner(ZNetView view)
        {
            return view && view.IsValid() && view.GetZDO() != null && view.IsOwner();
        }

        private static bool IsValidView(ZNetView view)
        {
            return view && view.IsValid() && view.GetZDO() != null;
        }

        private static GameObject GetItemPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            GameObject prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (!prefab && ZNetScene.instance)
            {
                prefab = ZNetScene.instance.GetPrefab(prefabName);
            }

            return prefab;
        }

        private static string GetSharedName(GameObject prefab)
        {
            return prefab ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name : null;
        }

        private static Piece GetPiece(Component component)
        {
            if (!component)
            {
                return null;
            }

            Piece piece = component.GetComponentInParent<Piece>();
            return piece ? piece : component.GetComponentInChildren<Piece>(true);
        }

        private static bool HasLocalAccess(Component component)
        {
            Piece piece = GetPiece(component);
            if (!piece)
            {
                return true;
            }

            return Player.m_localPlayer
                && PrivateArea.CheckAccess(piece.transform.position, 0f, flash: false);
        }

        private static bool ShouldBlockFollowupHold(Component source, bool hold)
        {
            if (!source)
            {
                blockedHoldSourceId = 0;
                return false;
            }

            int sourceId = source.GetInstanceID();
            if (hold)
            {
                return blockedHoldSourceId == sourceId;
            }

            blockedHoldSourceId = 0;
            return false;
        }

        private static void BlockFollowupHold(Component source)
        {
            blockedHoldSourceId = source ? source.GetInstanceID() : 0;
        }

        private static bool PrepareRequest(ZNetView view)
        {
            if (!IsValidView(view))
            {
                return false;
            }

            if (!view.HasOwner())
            {
                view.ClaimOwnership();
            }

            return view.HasOwner();
        }

        private static void RequestReturn(ZNetView view, Humanoid user, string rpcName)
        {
            if (!user || !PrepareRequest(view))
            {
                return;
            }

            view.InvokeRPC(rpcName, user.transform.position);
        }

        private static void RequestCookingSkillCredit(CookingStation station, int slot, string expectedPrefabName)
        {
            if (!station
                || slot < 0
                || string.IsNullOrEmpty(expectedPrefabName)
                || !PrepareRequest(station.m_nview))
            {
                return;
            }

            station.m_nview.InvokeRPC(RecordCookingSkillRpc, slot, expectedPrefabName);
        }

        private static bool CreateItem(
            GameObject prefab,
            Vector3 position,
            Vector3 direction,
            float force,
            bool cheated,
            out ItemDrop spawnedItem)
        {
            spawnedItem = null;
            ItemDrop prefabItem = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!prefabItem)
            {
                return false;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }
            direction.Normalize();

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                position,
                Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f));

            spawnedItem = instance.GetComponent<ItemDrop>();
            if (!spawnedItem)
            {
                UnityEngine.Object.Destroy(instance);
                return false;
            }

            ItemDrop.OnCreateNew(spawnedItem, cheated);

            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body)
            {
                body.linearVelocity = direction * force;
            }

            return true;
        }

        private static bool SpawnItem(
            GameObject prefab,
            Vector3 origin,
            Vector3 fallbackDirection,
            Vector3 userPoint,
            float force,
            bool cheated,
            out ItemDrop spawnedItem)
        {
            Vector3 direction = userPoint - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = fallbackDirection;
                direction.y = 0f;
            }
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }
            direction.Normalize();

            return CreateItem(
                prefab,
                origin + direction * 0.5f,
                direction,
                force,
                cheated,
                out spawnedItem);
        }

        private static T ResolveStationSwitch<T>(Switch switchRef, Func<T, Switch> getSwitch)
            where T : Component
        {
            if (!switchRef)
            {
                return null;
            }

            T station = switchRef.GetComponentInParent<T>();
            if (station && getSwitch(station) == switchRef)
            {
                return station;
            }

            if (switchRef.m_onUse == null)
            {
                return null;
            }

            foreach (Delegate callback in switchRef.m_onUse.GetInvocationList())
            {
                if (callback.Target is T callbackOwner && getSwitch(callbackOwner) == switchRef)
                {
                    return callbackOwner;
                }
            }

            return null;
        }

        internal static CookingStation ResolveCookingItemSwitch(Switch switchRef)
        {
            return ResolveStationSwitch<CookingStation>(switchRef, station => station.m_addFoodSwitch);
        }

        internal static CookingStation ResolveCookingFuelSwitch(Switch switchRef)
        {
            return ResolveStationSwitch<CookingStation>(switchRef, station => station.m_addFuelSwitch);
        }

        internal static Smelter ResolveSmelterInputSwitch(Switch switchRef)
        {
            return ResolveStationSwitch<Smelter>(switchRef, station => station.m_addOreSwitch);
        }

        internal static Smelter ResolveSmelterFuelSwitch(Switch switchRef)
        {
            return ResolveStationSwitch<Smelter>(switchRef, station => station.m_addWoodSwitch);
        }

        private static bool TryGetCookingItem(
            CookingStation station,
            out int slot,
            out GameObject prefab,
            out string sharedName,
            out bool cheated,
            out long author,
            out bool skillCredited)
        {
            slot = -1;
            prefab = null;
            sharedName = null;
            cheated = false;
            author = 0L;
            skillCredited = false;

            if (!station || !IsValidView(station.m_nview) || station.m_slots == null)
            {
                return false;
            }

            ZDO zdo = station.m_nview.GetZDO();
            float progressLimit = Mod.CookingStationItemReturnProgressLimit?.Value ?? 0f;
            for (int index = station.m_slots.Length - 1; index >= 0; --index)
            {
                station.GetSlot(index, out string itemName, out float cookedTime, out CookingStation.Status status, out bool slotCheated);
                if (string.IsNullOrEmpty(itemName) || status != CookingStation.Status.NotDone)
                {
                    continue;
                }

                CookingStation.ItemConversion conversion = station.GetItemConversion(itemName);
                if (conversion == null || !conversion.m_from || itemName != conversion.m_from.name)
                {
                    continue;
                }

                if (progressLimit > 0f
                    && conversion.m_cookTime > 0f
                    && cookedTime > conversion.m_cookTime * progressLimit)
                {
                    continue;
                }

                GameObject itemPrefab = GetItemPrefab(itemName);
                string itemSharedName = GetSharedName(itemPrefab);
                if (!itemPrefab || string.IsNullOrEmpty(itemSharedName))
                {
                    continue;
                }

                slot = index;
                prefab = itemPrefab;
                sharedName = itemSharedName;
                cheated = slotCheated && !PlayerProfile.s_bypassCheatChecks;
                author = zdo.GetLong(CookingAuthorKey(index), 0L);
                skillCredited = zdo.GetBool(CookingSkillKey(index), false);
                return true;
            }

            return false;
        }

        private static bool TryGetCookingFuel(
            CookingStation station,
            out GameObject prefab,
            out string sharedName,
            out bool cheated)
        {
            prefab = null;
            sharedName = null;
            cheated = false;

            if (!station
                || !IsValidView(station.m_nview)
                || !station.m_fuelItem
                || station.GetFuel() <= 0f)
            {
                return false;
            }

            GameObject fuelPrefab = station.m_fuelItem.gameObject;
            string fuelSharedName = GetSharedName(fuelPrefab);
            if (!fuelPrefab || string.IsNullOrEmpty(fuelSharedName))
            {
                return false;
            }

            prefab = fuelPrefab;
            sharedName = fuelSharedName;
            cheated = station.m_nview.GetZDO().GetBool(ZDOVars.s_cheated, false)
                && !PlayerProfile.s_bypassCheatChecks;
            return true;
        }

        private static bool TryGetFireplaceFuel(
            Fireplace station,
            out GameObject prefab,
            out string sharedName)
        {
            prefab = null;
            sharedName = null;

            if (!station
                || !IsValidView(station.m_nview)
                || station.m_infiniteFuel
                || !station.m_canRefill
                || !station.m_fuelItem
                || Mod.IsFireplaceExcluded(station)
                || station.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel) <= 0f)
            {
                return false;
            }

            GameObject fuelPrefab = station.m_fuelItem.gameObject;
            string fuelSharedName = GetSharedName(fuelPrefab);
            if (!fuelPrefab || string.IsNullOrEmpty(fuelSharedName))
            {
                return false;
            }

            prefab = fuelPrefab;
            sharedName = fuelSharedName;
            return true;
        }

        private static bool TryGetSmelterInput(
            Smelter station,
            out int queueIndex,
            out GameObject prefab,
            out string sharedName)
        {
            queueIndex = -1;
            prefab = null;
            sharedName = null;

            if (!station || !IsValidView(station.m_nview))
            {
                return false;
            }

            int queueSize = station.GetQueueSize();
            if (queueSize <= 0)
            {
                return false;
            }

            ZDO zdo = station.m_nview.GetZDO();
            int itemIndex = queueSize - 1;
            string prefabName = zdo.GetString("item" + itemIndex, "");
            GameObject itemPrefab = GetItemPrefab(prefabName);
            string itemSharedName = GetSharedName(itemPrefab);
            if (!itemPrefab || string.IsNullOrEmpty(itemSharedName))
            {
                return false;
            }

            queueIndex = itemIndex;
            prefab = itemPrefab;
            sharedName = itemSharedName;
            return true;
        }

        private static bool TryGetSmelterFuel(
            Smelter station,
            out GameObject prefab,
            out string sharedName)
        {
            prefab = null;
            sharedName = null;

            if (!station
                || !IsValidView(station.m_nview)
                || !station.m_fuelItem
                || Mod.IsSmelterFuelExcluded(station)
                || station.GetFuel() <= 0f)
            {
                return false;
            }

            GameObject fuelPrefab = station.m_fuelItem.gameObject;
            string fuelSharedName = GetSharedName(fuelPrefab);
            if (!fuelPrefab || string.IsNullOrEmpty(fuelSharedName))
            {
                return false;
            }

            prefab = fuelPrefab;
            sharedName = fuelSharedName;
            return true;
        }

        private static bool TryGetFermenterIngredient(
            Fermenter station,
            out GameObject prefab,
            out string sharedName,
            out bool cheated)
        {
            prefab = null;
            sharedName = null;
            cheated = false;

            if (!station || !IsValidView(station.m_nview) || station.GetStatus() != Fermenter.Status.Fermenting)
            {
                return false;
            }

            float progressLimit = Mod.FermenterIngredientReturnProgressLimit?.Value ?? 0f;
            if (progressLimit > 0f
                && station.m_fermentationDuration > 0f
                && station.GetFermentationTime() > station.m_fermentationDuration * progressLimit)
            {
                return false;
            }

            int content = station.GetContent();
            if (content == 0)
            {
                return false;
            }

            Fermenter.ItemConversion conversion = station.GetItemConversion(content);
            GameObject itemPrefab = conversion != null && conversion.m_from
                ? conversion.m_from.gameObject
                : ZNetScene.instance ? ZNetScene.instance.GetPrefab(content) : null;
            string itemSharedName = GetSharedName(itemPrefab);
            if (!itemPrefab || string.IsNullOrEmpty(itemSharedName))
            {
                return false;
            }

            prefab = itemPrefab;
            sharedName = itemSharedName;
            cheated = station.m_nview.GetZDO().GetBool(ZDOVars.s_cheatedQueued, false)
                && !PlayerProfile.s_bypassCheatChecks;
            return true;
        }

        private static bool TryGetTurretAmmo(Turret station, out GameObject prefab, out string sharedName)
        {
            prefab = null;
            sharedName = null;

            if (!station
                || !IsValidView(station.m_nview)
                || !station.m_returnAmmoOnDestroy
                || station.m_maxAmmo <= 0
                || station.GetAmmo() <= 0)
            {
                return false;
            }

            GameObject itemPrefab = GetItemPrefab(station.GetAmmoType());
            string itemSharedName = GetSharedName(itemPrefab);
            if (!itemPrefab || string.IsNullOrEmpty(itemSharedName))
            {
                return false;
            }

            prefab = itemPrefab;
            sharedName = itemSharedName;
            return true;
        }

        internal static bool CanReturnCookingItem(CookingStation station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.CookingStationItemReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetCookingItem(station, out _, out _, out sharedName, out _, out _, out _);
        }

        internal static bool CanReturnCookingFuel(CookingStation station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.CookingStationFuelReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetCookingFuel(station, out _, out sharedName, out _);
        }

        internal static bool CanReturnFireplaceFuel(Fireplace station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.FireplaceFuelReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetFireplaceFuel(station, out _, out sharedName);
        }

        internal static bool CanReturnSmelterInput(Smelter station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.SmelterInputReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetSmelterInput(station, out _, out _, out sharedName);
        }

        internal static bool CanReturnSmelterFuel(Smelter station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.SmelterFuelReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetSmelterFuel(station, out _, out sharedName);
        }

        internal static bool CanReturnFermenterIngredient(Fermenter station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.FermenterIngredientReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetFermenterIngredient(station, out _, out sharedName, out _);
        }

        internal static bool CanReturnTurretAmmo(Turret station, bool checkAccess, out string sharedName)
        {
            sharedName = null;
            return IsEnabled
                && Mod.TurretAmmoReturnEnabled?.Value == true
                && (!checkAccess || HasLocalAccess(station))
                && TryGetTurretAmmo(station, out _, out sharedName);
        }

        internal static bool TryGetReturnableItem(object source, out string sharedName)
        {
            sharedName = null;

            switch (source)
            {
                case CookingStation cookingStation:
                    return CanReturnCookingItem(cookingStation, checkAccess: true, out sharedName);
                case Fireplace fireplace:
                    return CanReturnFireplaceFuel(fireplace, checkAccess: true, out sharedName);
                case Smelter smelter:
                    return CanReturnSmelterInput(smelter, checkAccess: true, out sharedName);
                case Fermenter fermenter:
                    return CanReturnFermenterIngredient(fermenter, checkAccess: true, out sharedName);
                case Turret turret:
                    return CanReturnTurretAmmo(turret, checkAccess: true, out sharedName);
                case Switch switchRef:
                {
                    CookingStation cookingItemStation = ResolveCookingItemSwitch(switchRef);
                    if (cookingItemStation
                        && CanReturnCookingItem(cookingItemStation, checkAccess: true, out sharedName))
                    {
                        return true;
                    }

                    CookingStation cookingFuelStation = ResolveCookingFuelSwitch(switchRef);
                    if (cookingFuelStation
                        && CanReturnCookingFuel(cookingFuelStation, checkAccess: true, out sharedName))
                    {
                        return true;
                    }

                    Smelter smelterInputStation = ResolveSmelterInputSwitch(switchRef);
                    if (smelterInputStation
                        && CanReturnSmelterInput(smelterInputStation, checkAccess: true, out sharedName))
                    {
                        return true;
                    }

                    Smelter smelterFuelStation = ResolveSmelterFuelSwitch(switchRef);
                    return smelterFuelStation
                        && CanReturnSmelterFuel(smelterFuelStation, checkAccess: true, out sharedName);
                }
                default:
                    return false;
            }
        }

        private static bool SpawnCookingItem(
            CookingStation station,
            int slot,
            GameObject prefab,
            Vector3 userPoint,
            bool cheated)
        {
            if (slot < 0 || slot >= station.m_slots.Length || !station.m_slots[slot])
            {
                return false;
            }

            Transform source = station.m_spawnPoint ? station.m_spawnPoint : station.m_slots[slot];
            Vector3 direction;
            Vector3 position;
            if (station.m_spawnPoint)
            {
                direction = source.forward;
                position = source.position;
            }
            else
            {
                direction = userPoint - source.position;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    direction = station.transform.forward;
                    direction.y = 0f;
                }
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    direction = Vector3.forward;
                }
                direction.Normalize();
                position = source.position + direction * 0.5f;
            }

            if (!CreateItem(
                prefab,
                position,
                direction,
                station.m_spawnForce,
                cheated,
                out ItemDrop item))
            {
                return false;
            }

            if (station.m_spawnFullDurability)
            {
                item.m_itemData.m_durability = item.m_itemData.m_shared.m_maxDurability;
            }

            station.m_pickEffector.Create(item.transform.position, Quaternion.identity);
            return true;
        }

        private static void ReturnCookingItemOnOwner(CookingStation station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.CookingStationItemReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetCookingItem(
                    station,
                    out int slot,
                    out GameObject prefab,
                    out _,
                    out bool cheated,
                    out long author,
                    out bool skillCredited)
                || !SpawnCookingItem(station, slot, prefab, userPoint, cheated))
            {
                return;
            }

            station.SetSlot(slot, "", 0f, CookingStation.Status.NotDone, cheated: false);
            station.m_nview.InvokeRPC(ZNetView.Everybody, "RPC_SetSlotVisual", slot, "");
            Mod.LogInfo($"Returned cooking station item '{prefab.name}' from slot {slot}.");

            if (skillCredited
                && author != 0L
                && station.m_skill != Skills.SkillType.None
                && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(author, UndoSkillRpc, (int)station.m_skill);
            }
        }

        private static void ReturnCookingFuelOnOwner(CookingStation station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.CookingStationFuelReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetCookingFuel(station, out GameObject prefab, out _, out bool cheated))
            {
                return;
            }

            Transform source = station.m_addFuelSwitch ? station.m_addFuelSwitch.transform : station.transform;
            if (!SpawnItem(prefab, source.position, source.forward, userPoint, 2f, cheated, out _))
            {
                return;
            }

            station.SetFuel(Mathf.Max(0f, station.GetFuel() - 1f));
            bool active = IsCookingStationActive(station);
            bool hasFuel = station.GetFuel() > 0f;
            station.m_nview.InvokeRPC(ZNetView.Everybody, RefreshCookingStationRpc, active, hasFuel);
            Mod.LogInfo($"Returned cooking station fuel '{prefab.name}'.");
        }

        private static bool IsCookingStationActive(CookingStation station)
        {
            return (station.m_requireFire && station.IsFireLit())
                || (station.m_useFuel
                    && station.GetFuel() > 0f
                    && (station.m_useFueldWhileEmpty || station.HaveUncookedItem()));
        }

        private static void RefreshCookingStation(CookingStation station, bool active, bool hasFuel)
        {
            if (!station)
            {
                return;
            }

            station.UpdateVisual(active);
            if (station.m_haveFuelObject)
            {
                station.m_haveFuelObject.SetActive(hasFuel);
            }
            if (station.m_cookingObject)
            {
                station.m_cookingObject.SetActive(
                    !station.IsEverythingCooked()
                    && hasFuel
                    && !station.IsEmpty());
            }
        }

        private static void ReturnFireplaceFuelOnOwner(Fireplace station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.FireplaceFuelReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetFireplaceFuel(station, out GameObject prefab, out _))
            {
                return;
            }

            Transform source = station.m_enabledObject ? station.m_enabledObject.transform : station.transform;
            if (!SpawnItem(
                prefab,
                source.position,
                source.forward,
                userPoint,
                2f,
                cheated: false,
                out _))
            {
                return;
            }

            ZDO zdo = station.m_nview.GetZDO();
            float fuel = zdo.GetFloat(ZDOVars.s_fuel);
            zdo.Set(ZDOVars.s_fuel, Mathf.Max(0f, fuel - 1f));
            station.m_nview.InvokeRPC(ZNetView.Everybody, RefreshFireplaceRpc);
            Mod.LogInfo($"Returned fireplace fuel '{prefab.name}' from '{station.m_nview.GetPrefabName()}'.");
        }

        private static void RefreshFireplace(Fireplace station)
        {
            if (station)
            {
                station.UpdateState();
            }
        }

        private static void ReturnSmelterInputOnOwner(Smelter station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.SmelterInputReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetSmelterInput(
                    station,
                    out int queueIndex,
                    out GameObject prefab,
                    out _))
            {
                return;
            }

            Transform source = station.m_addOreSwitch ? station.m_addOreSwitch.transform : station.transform;
            if (!SpawnItem(prefab, source.position, source.forward, userPoint, 2f, cheated: false, out _))
            {
                return;
            }

            ZDO zdo = station.m_nview.GetZDO();
            zdo.Set("item" + queueIndex, "");
            zdo.Set(ZDOVars.s_queued, queueIndex);

            if (queueIndex == 0)
            {
                station.SetBakeTimer(0f);
                zdo.Set(ZDOVars.s_cheatedQueued, false);
            }

            BroadcastSmelterRefresh(station);
            Mod.LogInfo($"Returned smelter input '{prefab.name}' from queue index {queueIndex}.");
        }

        private static void ReturnSmelterFuelOnOwner(Smelter station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.SmelterFuelReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetSmelterFuel(station, out GameObject prefab, out _))
            {
                return;
            }

            Transform source = station.m_addWoodSwitch ? station.m_addWoodSwitch.transform : station.transform;
            if (!SpawnItem(prefab, source.position, source.forward, userPoint, 2f, cheated: false, out _))
            {
                return;
            }

            station.SetFuel(Mathf.Max(0f, station.GetFuel() - 1f));
            BroadcastSmelterRefresh(station);
            Mod.LogInfo($"Returned smelter fuel '{prefab.name}'.");
        }

        private static void BroadcastSmelterRefresh(Smelter station)
        {
            station.m_nview.InvokeRPC(
                ZNetView.Everybody,
                RefreshSmelterRpc,
                station.IsActive(),
                station.GetFuel() > 0f,
                station.GetQueueSize() > 0);
        }

        private static void RefreshSmelter(Smelter station, bool active, bool hasFuel, bool hasInput)
        {
            if (!station)
            {
                return;
            }

            if (station.m_enabledObject)
            {
                station.m_enabledObject.SetActive(active);
            }
            if (station.m_disabledObject)
            {
                station.m_disabledObject.SetActive(!active);
            }
            if (station.m_haveFuelObject)
            {
                station.m_haveFuelObject.SetActive(hasFuel);
            }
            if (station.m_haveOreObject)
            {
                station.m_haveOreObject.SetActive(hasInput);
            }
            if (station.m_noOreObject)
            {
                station.m_noOreObject.SetActive(!hasInput);
            }

            station.SetAnimation(active);
        }

        private static void ReturnFermenterIngredientOnOwner(Fermenter station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.FermenterIngredientReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetFermenterIngredient(station, out GameObject prefab, out _, out bool cheated))
            {
                return;
            }

            Transform source = station.m_outputPoint ? station.m_outputPoint : station.transform;
            if (!SpawnItem(prefab, source.position, source.forward, userPoint, 2f, cheated, out _))
            {
                return;
            }

            ZDO zdo = station.m_nview.GetZDO();
            zdo.Set(ZDOVars.s_content, 0);
            zdo.Set(ZDOVars.s_startTime, 0L);
            zdo.Set(ZDOVars.s_cheatedQueued, false);
            station.m_nview.InvokeRPC(ZNetView.Everybody, RefreshFermenterRpc);
            Mod.LogInfo($"Returned fermenter ingredient '{prefab.name}'.");
        }

        private static void RefreshFermenter(Fermenter station)
        {
            if (!station)
            {
                return;
            }

            if (station.m_fermentingObject)
            {
                station.m_fermentingObject.SetActive(false);
            }
            if (station.m_readyObject)
            {
                station.m_readyObject.SetActive(false);
            }
            if (station.m_topObject)
            {
                station.m_topObject.SetActive(false);
            }
        }

        private static void ReturnTurretAmmoOnOwner(Turret station, Vector3 userPoint)
        {
            if (!IsEnabled
                || Mod.TurretAmmoReturnEnabled?.Value != true
                || !IsValidOwner(station.m_nview)
                || !TryGetTurretAmmo(station, out GameObject prefab, out _))
            {
                return;
            }

            if (!SpawnItem(
                prefab,
                station.transform.position + Vector3.up,
                station.transform.forward,
                userPoint,
                2f,
                cheated: false,
                out _))
            {
                return;
            }

            int remainingAmmo = Mathf.Max(0, station.GetAmmo() - 1);
            ZDO zdo = station.m_nview.GetZDO();
            zdo.Set(ZDOVars.s_ammo, remainingAmmo);

            string ammoType = station.GetAmmoType();
            if (remainingAmmo == 0 && !station.m_defaultAmmo)
            {
                zdo.Set(ZDOVars.s_ammoType, "");
                ammoType = "";
            }

            station.m_nview.InvokeRPC(ZNetView.Everybody, RefreshTurretRpc, remainingAmmo, ammoType);
            Mod.LogInfo($"Returned turret ammunition '{prefab.name}', remaining: {remainingAmmo}.");
        }

        private static void RefreshTurret(Turret station, int remainingAmmo, string ammoType)
        {
            if (!station)
            {
                return;
            }

            station.m_lastAmmo = null;
            bool showAmmo = remainingAmmo > 0 && !station.IsCoolingDown();
            foreach (Turret.AmmoType allowedAmmo in station.m_allowedAmmo)
            {
                if (allowedAmmo.m_visual)
                {
                    allowedAmmo.m_visual.SetActive(
                        showAmmo
                        && allowedAmmo.m_ammo
                        && allowedAmmo.m_ammo.name == ammoType);
                }
            }
        }

        private static bool IsMatchingCookingSlot(
            CookingStation station,
            ZDO zdo,
            int slot,
            long sender,
            string expectedPrefabName)
        {
            if (slot < 0 || station.m_slots == null || slot >= station.m_slots.Length)
            {
                return false;
            }

            station.GetSlot(slot, out string itemName, out _, out CookingStation.Status status, out _);
            return itemName == expectedPrefabName
                && status == CookingStation.Status.NotDone
                && zdo.GetLong(CookingAuthorKey(slot), 0L) == sender;
        }

        private static void MarkCookingSkillCreditOnOwner(
            CookingStation station,
            long sender,
            int requestedSlot,
            string expectedPrefabName)
        {
            if (!IsValidOwner(station.m_nview)
                || station.m_skill == Skills.SkillType.None)
            {
                return;
            }

            if (string.IsNullOrEmpty(expectedPrefabName))
            {
                return;
            }

            ZDO zdo = station.m_nview.GetZDO();
            long lastAuthor = zdo.GetLong(LastCookingAuthorKey, 0L);
            int lastSlot = zdo.GetInt(LastCookingSlotKey, -1);

            int slot = IsMatchingCookingSlot(station, zdo, lastSlot, sender, expectedPrefabName)
                ? lastSlot
                : requestedSlot;
            if (!IsMatchingCookingSlot(station, zdo, slot, sender, expectedPrefabName))
            {
                return;
            }

            zdo.Set(CookingSkillKey(slot), true);
            if (lastAuthor == sender && lastSlot == slot)
            {
                zdo.Set(LastCookingAuthorKey, 0L);
                zdo.Set(LastCookingSlotKey, -1);
            }
        }

        private static void UndoStationSkill(long sender, int skill)
        {
            if (!Enum.IsDefined(typeof(Skills.SkillType), skill)
                || skill == (int)Skills.SkillType.None
                || !Player.m_localPlayer)
            {
                return;
            }

            Player.m_localPlayer.RaiseSkill((Skills.SkillType)skill, -0.4f);
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Awake))]
        private static class CookingStation_Awake_RegisterRpcs
        {
            private static void Postfix(CookingStation __instance)
            {
                if (!IsValidView(__instance.m_nview))
                {
                    return;
                }

                __instance.m_nview.Register<Vector3>(
                    ReturnCookingItemRpc,
                    (sender, userPoint) => ReturnCookingItemOnOwner(__instance, userPoint));
                __instance.m_nview.Register<Vector3>(
                    ReturnCookingFuelRpc,
                    (sender, userPoint) => ReturnCookingFuelOnOwner(__instance, userPoint));
                __instance.m_nview.Register<bool, bool>(
                    RefreshCookingStationRpc,
                    (sender, active, hasFuel) => RefreshCookingStation(__instance, active, hasFuel));
                __instance.m_nview.Register<int, string>(
                    RecordCookingSkillRpc,
                    (sender, slot, expectedPrefabName) => MarkCookingSkillCreditOnOwner(
                        __instance,
                        sender,
                        slot,
                        expectedPrefabName));
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
        private static class Fireplace_Awake_RegisterRpcs
        {
            private static void Postfix(Fireplace __instance)
            {
                if (!IsValidView(__instance.m_nview))
                {
                    return;
                }

                __instance.m_nview.Register<Vector3>(
                    ReturnFireplaceFuelRpc,
                    (sender, userPoint) => ReturnFireplaceFuelOnOwner(__instance, userPoint));
                __instance.m_nview.Register(
                    RefreshFireplaceRpc,
                    sender => RefreshFireplace(__instance));
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.Awake))]
        private static class Smelter_Awake_RegisterRpcs
        {
            private static void Postfix(Smelter __instance)
            {
                if (!IsValidView(__instance.m_nview))
                {
                    return;
                }

                __instance.m_nview.Register<Vector3>(
                    ReturnSmelterInputRpc,
                    (sender, userPoint) => ReturnSmelterInputOnOwner(__instance, userPoint));
                __instance.m_nview.Register<Vector3>(
                    ReturnSmelterFuelRpc,
                    (sender, userPoint) => ReturnSmelterFuelOnOwner(__instance, userPoint));
                __instance.m_nview.Register<bool, bool, bool>(
                    RefreshSmelterRpc,
                    (sender, active, hasFuel, hasInput) => RefreshSmelter(
                        __instance,
                        active,
                        hasFuel,
                        hasInput));
            }
        }

        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Awake))]
        private static class Fermenter_Awake_RegisterRpcs
        {
            private static void Postfix(Fermenter __instance)
            {
                if (!IsValidView(__instance.m_nview))
                {
                    return;
                }

                __instance.m_nview.Register<Vector3>(
                    ReturnFermenterIngredientRpc,
                    (sender, userPoint) => ReturnFermenterIngredientOnOwner(__instance, userPoint));
                __instance.m_nview.Register(
                    RefreshFermenterRpc,
                    sender => RefreshFermenter(__instance));
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.Awake))]
        private static class Turret_Awake_RegisterRpcs
        {
            private static void Postfix(Turret __instance)
            {
                if (!IsValidView(__instance.m_nview))
                {
                    return;
                }

                __instance.m_nview.Register<Vector3>(
                    ReturnTurretAmmoRpc,
                    (sender, userPoint) => ReturnTurretAmmoOnOwner(__instance, userPoint));
                __instance.m_nview.Register<int, string>(
                    RefreshTurretRpc,
                    (sender, remainingAmmo, ammoType) => RefreshTurret(
                        __instance,
                        remainingAmmo,
                        ammoType));
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.RPC_AddItem))]
        private static class CookingStation_RPC_AddItem_CaptureAuthor
        {
            private static void Prefix(long sender, out long __state)
            {
                __state = activeCookingAuthor;
                activeCookingAuthor = sender;
            }

            private static void Finalizer(long __state)
            {
                activeCookingAuthor = __state;
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.SetSlot))]
        private static class CookingStation_SetSlot_TrackReturnMetadata
        {
            private static void Postfix(CookingStation __instance, int slot, string itemName)
            {
                if (!IsValidOwner(__instance.m_nview))
                {
                    return;
                }

                ZDO zdo = __instance.m_nview.GetZDO();
                if (string.IsNullOrEmpty(itemName))
                {
                    zdo.Set(CookingAuthorKey(slot), 0L);
                    zdo.Set(CookingSkillKey(slot), false);
                    if (zdo.GetInt(LastCookingSlotKey, -1) == slot)
                    {
                        zdo.Set(LastCookingAuthorKey, 0L);
                        zdo.Set(LastCookingSlotKey, -1);
                    }
                    return;
                }

                if (activeCookingAuthor != 0L)
                {
                    zdo.Set(CookingAuthorKey(slot), activeCookingAuthor);
                    zdo.Set(CookingSkillKey(slot), false);
                    zdo.Set(LastCookingAuthorKey, activeCookingAuthor);
                    zdo.Set(LastCookingSlotKey, slot);
                }
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.OnInteract))]
        private static class CookingStation_OnInteract_RecordSkillCredit
        {
            private static void Prefix(
                CookingStation __instance,
                Humanoid user,
                out CookingInteractionState __state)
            {
                bool hadDoneItem = __instance.HaveDoneItem();
                ItemDrop.ItemData candidate = !hadDoneItem && user
                    ? __instance.FindCookableItem(user.GetInventory())
                    : null;

                __state = new CookingInteractionState
                {
                    HadDoneItem = hadDoneItem,
                    FreeSlot = __instance.GetFreeSlot(),
                    ExpectedPrefabName = candidate?.m_dropPrefab?.name
                };
            }

            private static void Postfix(CookingStation __instance, bool __result, CookingInteractionState __state)
            {
                if (__result
                    && !__state.HadDoneItem
                    && __state.FreeSlot >= 0
                    && !string.IsNullOrEmpty(__state.ExpectedPrefabName)
                    && __instance.m_skill != Skills.SkillType.None)
                {
                    RequestCookingSkillCredit(
                        __instance,
                        __state.FreeSlot,
                        __state.ExpectedPrefabName);
                }
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
        private static class CookingStation_Interact_ReturnStoredItem
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                CookingStation __instance,
                Humanoid user,
                bool hold,
                bool alt,
                bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    return false;
                }
                if (ShouldBlockFollowupHold(__instance, hold))
                {
                    return false;
                }
                if (hold || !alt || __instance.m_addFoodSwitch)
                {
                    return true;
                }
                if (!CanReturnCookingItem(__instance, checkAccess: true, out _))
                {
                    return true;
                }

                RequestReturn(__instance.m_nview, user, ReturnCookingItemRpc);
                BlockFollowupHold(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Interact))]
        private static class Fireplace_Interact_ReturnFuel
        {
            [HarmonyBefore("shudnal.MyLittleUI", "shudnal.AnyWood")]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                Fireplace __instance,
                Humanoid user,
                bool hold,
                bool alt,
                bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    return false;
                }
                if (ShouldBlockFollowupHold(__instance, hold))
                {
                    return false;
                }
                if (hold || !alt || !CanReturnFireplaceFuel(__instance, checkAccess: true, out _))
                {
                    return true;
                }

                RequestReturn(__instance.m_nview, user, ReturnFireplaceFuelRpc);
                BlockFollowupHold(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Switch), nameof(Switch.Interact))]
        private static class Switch_Interact_ReturnStoredItem
        {
            [HarmonyBefore("shudnal.MyLittleUI", "shudnal.AnyWood")]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                Switch __instance,
                Humanoid character,
                bool hold,
                bool alt,
                bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    return false;
                }
                if (ShouldBlockFollowupHold(__instance, hold))
                {
                    return false;
                }
                if (hold || !alt)
                {
                    return true;
                }

                CookingStation cookingItemStation = ResolveCookingItemSwitch(__instance);
                if (cookingItemStation
                    && CanReturnCookingItem(cookingItemStation, checkAccess: true, out _))
                {
                    RequestReturn(cookingItemStation.m_nview, character, ReturnCookingItemRpc);
                    BlockFollowupHold(__instance);
                    return false;
                }

                CookingStation cookingFuelStation = ResolveCookingFuelSwitch(__instance);
                if (cookingFuelStation
                    && CanReturnCookingFuel(cookingFuelStation, checkAccess: true, out _))
                {
                    RequestReturn(cookingFuelStation.m_nview, character, ReturnCookingFuelRpc);
                    BlockFollowupHold(__instance);
                    return false;
                }

                Smelter smelterInputStation = ResolveSmelterInputSwitch(__instance);
                if (smelterInputStation
                    && CanReturnSmelterInput(smelterInputStation, checkAccess: true, out _))
                {
                    RequestReturn(smelterInputStation.m_nview, character, ReturnSmelterInputRpc);
                    BlockFollowupHold(__instance);
                    return false;
                }

                Smelter smelterFuelStation = ResolveSmelterFuelSwitch(__instance);
                if (smelterFuelStation
                    && CanReturnSmelterFuel(smelterFuelStation, checkAccess: true, out _))
                {
                    RequestReturn(smelterFuelStation.m_nview, character, ReturnSmelterFuelRpc);
                    BlockFollowupHold(__instance);
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.Interact))]
        private static class Fermenter_Interact_ReturnIngredient
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                Fermenter __instance,
                Humanoid user,
                bool hold,
                bool alt,
                bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    return false;
                }
                if (ShouldBlockFollowupHold(__instance, hold))
                {
                    return false;
                }
                if (hold || !alt || !CanReturnFermenterIngredient(__instance, checkAccess: true, out _))
                {
                    return true;
                }

                RequestReturn(__instance.m_nview, user, ReturnFermenterIngredientRpc);
                BlockFollowupHold(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.Interact))]
        private static class Turret_Interact_ReturnAmmo
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(
                Turret __instance,
                Humanoid character,
                bool hold,
                bool alt,
                bool __runOriginal)
            {
                if (!__runOriginal)
                {
                    return false;
                }
                if (ShouldBlockFollowupHold(__instance, hold))
                {
                    return false;
                }
                if (hold || !alt || !CanReturnTurretAmmo(__instance, checkAccess: true, out _))
                {
                    return true;
                }

                RequestReturn(__instance.m_nview, character, ReturnTurretAmmoRpc);
                BlockFollowupHold(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Start_RegisterRoutedRpc
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null || ReferenceEquals(registeredRoutedRpc, ZRoutedRpc.instance))
                {
                    return;
                }

                registeredRoutedRpc = ZRoutedRpc.instance;
                registeredRoutedRpc.Register<int>(UndoSkillRpc, UndoStationSkill);
            }
        }
    }
}
