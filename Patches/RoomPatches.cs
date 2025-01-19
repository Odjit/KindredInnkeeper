using HarmonyLib;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using Unity.Collections;
using Unity.Content;
using Unity.Entities;

namespace KindredInnkeeper.Patches;

[HarmonyPatch(typeof(OpenDoorSystem), nameof(OpenDoorSystem.OnUpdate))]
public static class PreventDoorOpenPatch
{
    public static void Prefix(OpenDoorSystem __instance)
    {
        var entities = __instance.__query_1834203323_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
		{
			var doorOpener = entity.Read<EntityOwner>().Owner;
			var doorUser = doorOpener.Read<PlayerCharacter>().UserEntity;
			var clanRole = doorUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var spellTarget = entity.Read<SpellTarget>();
            var door = spellTarget.Target.GetEntityOnServer();

            var parentBuffer = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(door);
            if (parentBuffer.Length == 0) continue;

            var entrance = parentBuffer[0].ParentEntity.GetEntityOnServer();
            if (!entrance.Has<CastleRoomWall>()) continue;
            if (GetRoomOwnerFromWall(entrance.Read<CastleRoomWall>(), out var roomOwner, out var room))
            {
                Core.InnService.GetRoomIn(entity.Read<EntityOwner>().Owner, out var playerInRoom);
                if (!doorOpener.Equals(roomOwner) && !roomOwner.Equals(Entity.Null) && !room.Equals(playerInRoom))
                {
                    var user = doorOpener.Read<PlayerCharacter>().UserEntity.Read<User>();
                    var roomOwnerName = roomOwner.Read<PlayerCharacter>().Name;
                    ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, user, $"This is <color=white>{roomOwnerName}</color>'s room. You cannot open or close the door to a room that isn't yours.");

                    entity.Write(new SpellTarget { DestroyIfNotInteractable = true, Target = Entity.Null });
                    continue;
                }
            }
        }
        entities.Dispose();
    }

    static bool GetRoomOwnerFromWall(CastleRoomWall castleRoomWall, out Entity owner, out Entity room)
    {
        if (!castleRoomWall.FloorNorth.Equals(Entity.Null) &&
            Core.InnService.GetRoomOwner(castleRoomWall.FloorNorth.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer(), out owner))
        {
            room = castleRoomWall.FloorNorth.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
            return true;
        }
        if (!castleRoomWall.FloorEast.Equals(Entity.Null) &&
            Core.InnService.GetRoomOwner(castleRoomWall.FloorEast.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer(), out owner))
        {
            room = castleRoomWall.FloorEast.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
            return true;
        }
        if (!castleRoomWall.FloorSouth.Equals(Entity.Null) &&
            Core.InnService.GetRoomOwner(castleRoomWall.FloorSouth.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer(), out owner))
        {
            room = castleRoomWall.FloorSouth.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
            return true;
        }
        if (!castleRoomWall.FloorWest.Equals(Entity.Null) &&
            Core.InnService.GetRoomOwner(castleRoomWall.FloorWest.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer(), out owner))
        {
            room = castleRoomWall.FloorWest.Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();
            return true;
        }
        room = Entity.Null;
        owner = Entity.Null;
        return false;
    }
}


[HarmonyPatch(typeof(MoveItemBetweenInventoriesSystem), nameof(MoveItemBetweenInventoriesSystem.OnUpdate))]
public static class PreventInventoryMovements
{
    public static void Prefix(MoveItemBetweenInventoriesSystem __instance)
    {
        var entities = __instance._MoveItemBetweenInventoriesEventQuery.ToEntityArray(Allocator.Temp);
        foreach(var entity in entities)
		{
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var moveItemBetweenInventoriesEvent = entity.Read<MoveItemBetweenInventoriesEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(moveItemBetweenInventoriesEvent.FromInventory, out var roomOwner) &&
                !roomOwner.Equals(fromCharacter) ||
                Core.InnService.GetRoomOwnerForNetworkId(moveItemBetweenInventoriesEvent.ToInventory, out roomOwner) &&
                !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(MoveAllItemsBetweenInventoriesSystem), nameof(MoveAllItemsBetweenInventoriesSystem.OnUpdate))]
public static class PreventInventoryMovementsAll
{
    public static void Prefix(MoveAllItemsBetweenInventoriesSystem __instance)
    {
        var entities = __instance.__query_133601413_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
		{
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var moveAllItemsBetweenInventoriesEvent = entity.Read<MoveAllItemsBetweenInventoriesEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(moveAllItemsBetweenInventoriesEvent.FromInventory, out var roomOwner) &&
                               !roomOwner.Equals(fromCharacter) ||
                                              Core.InnService.GetRoomOwnerForNetworkId(moveAllItemsBetweenInventoriesEvent.ToInventory, out roomOwner) &&
                                                             !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(SmartMergeItemsBetweenInventoriesSystem), nameof(SmartMergeItemsBetweenInventoriesSystem.OnUpdate))]
public static class PreventSmartMerge
{
    public static void Prefix(SmartMergeItemsBetweenInventoriesSystem __instance)
    {
        var entities = __instance.__query_133601510_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
		{
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var smartMergeItemsBetweenInventoriesEvent = entity.Read<SmartMergeItemsBetweenInventoriesEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(smartMergeItemsBetweenInventoriesEvent.FromInventory, out var roomOwner) &&
                               !roomOwner.Equals(fromCharacter) ||
                                              Core.InnService.GetRoomOwnerForNetworkId(smartMergeItemsBetweenInventoriesEvent.ToInventory, out roomOwner) &&
                                                             !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(SortAllInventoriesSystem), nameof(SortAllInventoriesSystem.OnUpdate))]
public static class PreventSortAll
{
    public static void Prefix(SortAllInventoriesSystem __instance)
    {
        var entities = __instance.__query_133601617_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var smartMergeItemsBetweenInventoriesEvent = entity.Read<SortAllInventoriesEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(smartMergeItemsBetweenInventoriesEvent.Inventory, out var roomOwner) &&
                               !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(SortSingleInventorySystem), nameof(SortSingleInventorySystem.OnUpdate))]
public static class PreventSortSingle
{
    public static void Prefix(SortSingleInventorySystem __instance)
    {
        var entities = __instance.__query_133601574_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
		{
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var sortSingleInventoryEvent = entity.Read<SortSingleInventoryEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(sortSingleInventoryEvent.Inventory, out var roomOwner) &&
                               !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(SplitItemSystem), nameof(SplitItemSystem.OnUpdate))]
public static class PreventSplit
{
    public static void Prefix(SplitItemSystem __instance)
    {
        var entities = __instance._Query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
		{
			var fromUser = entity.Read<FromCharacter>().User;
			var clanRole = fromUser.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var splitItemEvent = entity.Read<SplitItemEvent>();
            var fromCharacter = entity.Read<FromCharacter>().Character;
            if (Core.InnService.GetRoomOwnerForNetworkId(splitItemEvent.Inventory, out var roomOwner) &&
                               !roomOwner.Equals(fromCharacter))
            {
                Core.EntityManager.DestroyEntity(entity);
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(BindCoffinSystem), nameof(BindCoffinSystem.OnUpdate))]
public static class PreventCoffinBinding
{
    public static void Prefix(BindCoffinSystem __instance)
    {
        var entities = __instance.__query_796452872_0.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            var spellTarget = entity.Read<SpellTarget>();
            var owner = entity.Read<EntityOwner>().Owner;
            var room = spellTarget.Target.GetEntityOnServer().Read<CastleRoomConnection>().RoomEntity.GetEntityOnServer();

            if (Core.InnService.GetRoomOwner(room, out var roomOwner) && !roomOwner.Equals(owner))
            {
                entity.Write(new SpellTarget { DestroyIfNotInteractable = true, Target = Entity.Null });
                continue;
            }
        }
    }
}

[HarmonyPatch(typeof(NameableInteractableSystem), nameof(NameableInteractableSystem.OnUpdate))]
public static class PreventRenames
{
	public static void Prefix(NameableInteractableSystem __instance)
	{
		var entities = __instance._RenameQuery.ToEntityArray(Allocator.Temp);
		foreach (var entity in entities)
		{
			var fromCharacter = entity.Read<FromCharacter>().User;
			var clanRole = fromCharacter.Read<ClanRole>().Value;
			if (clanRole == ClanRoleEnum.Leader) continue;

			var renameInteractable = entity.Read<InteractEvents_Client.RenameInteractable>();
			if (Core.InnService.GetRoomOwnerForNetworkId(renameInteractable.InteractableId, out var _))
			{
				Core.EntityManager.DestroyEntity(entity);
			}
		}
	}
}


