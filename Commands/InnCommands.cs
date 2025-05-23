using Il2CppSystem;
using KindredInnkeeper.Commands.Converters;
using KindredInnkeeper.Services;
using KindreInnkeeper.Services;
using Newtonsoft.Json;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Tiles;
using Stunlock.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using VampireCommandFramework;


namespace KindredInnkeeper.Commands;

[CommandGroup("inn")]
internal class InnCommands
{
	[Command("create", description: "Makes your current clan the Inn if one doesn't exist", adminOnly: true)]
	public static void CreateInn(ChatCommandContext ctx)
	{
		var clanEntity = Core.InnService.GetInnClan();
		if (clanEntity != Entity.Null)
		{
			ctx.Reply($"The Inn already exists with the clan <color=white>{clanEntity.Read<ClanTeam>().Name}</color>.");
			return;
		}

		if (Core.InnService.MakeCurrentClanAnInn(ctx.Event.SenderCharacterEntity))
		{
			clanEntity = Core.InnService.GetInnClan();
			ctx.Reply($"Successfully made the clan <color=white>{clanEntity.Read<ClanTeam>().Name}</color> the Inn.");
		}
		else
		{
			ctx.Reply("You are not in a clan.");
		}
	}

	[Command("remove", description: "Removes the currently set Inn clan", adminOnly: true)]
	public static void RemoveInn(ChatCommandContext ctx)
	{
		if (Core.InnService.RemoveInnClan())
		{
			ctx.Reply($"Successfully removed the Inn clan.");
		}
		else
		{
			ctx.Reply($"The Inn does not exist.");
		}
	}

	[Command("join", description: "Adds self to a clan of the Inn", adminOnly: false)]
    public static void AddToClan(ChatCommandContext ctx)
    {
        var userToAddEntity = ctx.Event.SenderUserEntity;
        var user = userToAddEntity.Read<User>();
        if (!user.ClanEntity.Equals(NetworkedEntity.Empty))
        {
            var clanTeam = user.ClanEntity.GetEntityOnServer().Read<ClanTeam>();
            ctx.Reply($"You are in an existing clan of '{clanTeam.Name}'");
            return;
        }

        //if the user already owns a territory, they cannot join the clan
        foreach (var castleTerritoryEntity in Helper.GetEntitiesByComponentType<CastleTerritory>())
        {
            var castleTerritory = castleTerritoryEntity.Read<CastleTerritory>();
            if (castleTerritory.CastleHeart.Equals(Entity.Null)) continue;

            var userOwner = castleTerritory.CastleHeart.Read<UserOwner>();
            if (userOwner.Owner.GetEntityOnServer().Equals(userToAddEntity))
            {
                ctx.Reply("You cannot join the Inn while owning a territory.");
                return;
            }
		}


		var clanEntity = Core.InnService.GetInnClan();
        if (clanEntity == Entity.Null)
        {
            ctx.Reply($"No clan is set as the Inn");
            return;
        }

		userToAddEntity.Write(new ClanRole() { Value = ClanRoleEnum.Member });

		TeamUtility.AddUserToClan(Core.EntityManager, clanEntity, userToAddEntity, ref user, CastleHeartLimitType.User);
        userToAddEntity.Write(user);

        var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clanEntity);
        var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clanEntity);

        for (var i = 0; i < members.Length; ++i)
        {
            var member = members[i];
            var userBufferEntry = userBuffer[i];
            if (userBufferEntry.UserEntity.Equals(userToAddEntity))
            {
                member.ClanRole = ClanRoleEnum.Member;
                members[i] = member;
            }
        }

        ctx.Reply($"<color=white>You have joined the Inn.</color>");
    }

	[Command("info", description: "Displays the information of the Inn", adminOnly: false)]
	public static void DisplayInnInfo(ChatCommandContext ctx)
	{
		var info = Core.ConfigSettings.InnInfo;
		ctx.Reply(string.Join("\n", info));
	}

	[Command("inforeload", description: "Reloads the Inn information", adminOnly: true)]
	public static void ReloadInnInfo(ChatCommandContext ctx)
	{
		Core.ConfigSettings.LoadConfig();
		ctx.Reply("Reloaded Inn information.");
	}

	[Command("help", description: "Displays the commands available for the Inn", adminOnly: false)]
	public static void DisplayHelp(ChatCommandContext ctx)
	{
		var help = new StringBuilder();
		help.AppendLine("<color=yellow>Inn Helpdesk!</color>");
		help.AppendLine("<color=green>.inn info</color> - Info on the Inn.");
		help.AppendLine("<color=green>.inn vacancy</color> - Reports occupancy amounts in the Inn.");
		help.AppendLine("<color=green>.inn claimroom</color> - Use this while standing in a vacant room in an inn. Empty rooms have a purple rune doormat.");
		help.AppendLine("<color=green>.inn leaveroom</color> - Use this to check out of your room.");
		help.AppendLine("<color=green>.inn findroom</color> - Creates a spotlight to your door.");
		help.AppendLine("<color=green>.inn quests</color> - Complete beginning shelter quests.");
		ctx.Reply(help.ToString());
	}

	[Command("guests", description: "Displays the guests of the Inn", adminOnly: true)]
    public static void DisplayGuests(ChatCommandContext ctx)
    {
		var roomOwners = Core.InnService.GetRoomOwners();
        ctx.Reply("Inn Guests: " + string.Join(", ", roomOwners.Select(x => x.Read<PlayerCharacter>().Name)));

	}
    
    [Command("quests", description: "Complete beginning shelter quests", adminOnly: false)]
    public static void Quests(ChatCommandContext ctx)
    {
 
        var entityCommandBuffer = Core.EntityCommandBufferSystem.CreateCommandBuffer();
        PrefabGUID settling = new(1694767961); // Settling
        PrefabGUID fortify = new(-1899098914); // Fortify
        PrefabGUID shelter = new(-122882616); // Shelter
        PrefabGUID upgradecastleheartt02 = new(1668809517); // UpgradeCastleHeart_Tier02


        Entity userEntity = ctx.Event.SenderUserEntity;
        Entity characterEntity = ctx.Event.SenderCharacterEntity;
        Entity achievementOwnerEntity = userEntity.Read<AchievementOwner>().Entity._Entity;

        Core.ClaimAchievementSystem.CompleteAchievement(entityCommandBuffer, settling, userEntity, characterEntity, achievementOwnerEntity, false, true);
        Core.ClaimAchievementSystem.CompleteAchievement(entityCommandBuffer, fortify, userEntity, characterEntity, achievementOwnerEntity, false, true);
        Core.ClaimAchievementSystem.CompleteAchievement(entityCommandBuffer, shelter, userEntity, characterEntity, achievementOwnerEntity, false, true);
        Core.ClaimAchievementSystem.CompleteAchievement(entityCommandBuffer, upgradecastleheartt02, userEntity, characterEntity, achievementOwnerEntity, false, true);
        ctx.Reply("Completed initial shelter journal quests.");
    }

    [Command("claimroom", description:"Use this while standing in a vacant room in an inn")]
    public static void ClaimRoom(ChatCommandContext ctx)
    {
		if (Core.InnService.HasClaimedARoom(ctx.Event.SenderCharacterEntity))
		{
			ctx.Reply("You have already claimed a room. Leave your current room before claiming another one.");
			return;
		}

        if (Core.InnService.GetRoomIn(ctx.Event.SenderCharacterEntity, out var roomIn))
        {
			Core.InnService.ClearRoom(roomIn);
			switch (Core.InnService.SetRoomOwnerIfEmpty(roomIn, ctx.Event.SenderCharacterEntity))
            {
                case InnService.RoomSetFailure.None:
                    ctx.Reply("You have claimed this room.");
                    return;
                case InnService.RoomSetFailure.AlreadyClaimed:
                    Core.InnService.GetRoomOwner(roomIn, out var roomOwner);
                    ctx.Reply($"This room is already claimed by <color=white>{roomOwner.Read<PlayerCharacter>().Name}</color>");
                    return;
                case InnService.RoomSetFailure.NotInInnClan:
                    ctx.Reply("You are not in the Inn clan.");
                    return;
            }
        }
        ctx.Reply("You are not standing in a room at the Inn.");
    }


	readonly static Dictionary<Entity, double> leaveRoomTimer = [];
	[Command("leaveroom")]
	public static void LeaveRoom(ChatCommandContext ctx)
	{
		// Warn them the first time they execute this command
		if (!leaveRoomTimer.TryGetValue(ctx.Event.SenderCharacterEntity, out var lastTime) || lastTime + 60 < Core.ServerTime)
		{
			ctx.Reply("Warning you will lose all items within your claimed room. Run this command again to leave your room");
			if (leaveRoomTimer.ContainsKey(ctx.Event.SenderCharacterEntity))
				leaveRoomTimer[ctx.Event.SenderCharacterEntity] = Core.ServerTime;
			else
				leaveRoomTimer.Add(ctx.Event.SenderCharacterEntity, Core.ServerTime);
			return;
		}
		else
		{
			if (Core.InnService.LeaveRoom(ctx.Event.SenderCharacterEntity))
			{
				ctx.Reply("You have checked out of your room.");
			}
			else
			{
				ctx.Reply("You haven't claimed a room at the Inn yet.");
			}
		}
	}

	[Command("addroom", description: "Adds a room to the Inn", adminOnly: true)]
	public static void AddRoom(ChatCommandContext ctx)
	{
		var result = Core.InnService.AddRoomToInn(ctx.Event.SenderCharacterEntity);
		if (result != "")
		{
			ctx.Reply("Failed to add a room to the Inn: " + result);
			return;
		}
		ctx.Reply("Added a room to the Inn.");
	}

	[Command("removeroom", description: "Removes a room from the Inn", adminOnly: true)]
	public static void RemoveRoom(ChatCommandContext ctx)
	{
		if (Core.InnService.RemoveRoomFromInn(ctx.Event.SenderCharacterEntity))
		{
			ctx.Reply("Removed a room from the Inn.");
			return;
		}
		ctx.Reply("Not in a room of the Inn.");
	}


	[Command("vacancy", description: "Reports occupancy amounts in the Inn", adminOnly: false)]
    public static void ListRooms(ChatCommandContext ctx)
    {
        int freeRoomCount = Core.InnService.GetFreeRoomCount();
        int totalRoomCount = Core.InnService.GetRoomCount();

        string roomPlural = freeRoomCount == 1 ? "" : "s";
        ctx.Reply($"{freeRoomCount} free room{roomPlural} out of {totalRoomCount} room{(totalRoomCount == 1 ? "" : "s")} in the inn.\n");

        if (freeRoomCount == 0)
        {
            ctx.Reply("There is no room available in the inn.");
            return;
        }
    }

	[Command("setroomowner", "sro", adminOnly: true)]
    public static void SetRoomOwner(ChatCommandContext ctx, FoundPlayer player)
    {
        if (Core.InnService.GetRoomIn(ctx.Event.SenderCharacterEntity, out var roomIn))
        {
            switch(Core.InnService.SetRoomOwner(roomIn, player.Value.CharEntity))
            {
                case InnService.RoomSetFailure.None:
                    ctx.Reply($"Set the owner of the room to <color=white>{player.Value.UserEntity.Read<User>().CharacterName}</color>.");
                    return;
                case InnService.RoomSetFailure.NotInInnClan:
                    ctx.Reply($"<color=white>{player.Value.UserEntity.Read<User>().CharacterName} is not in the Inn clan.");
                    return;
            }
        }
        ctx.Reply("You are not standing in a room at the Inn.");
    }

    [Command("roomowner", "ro", adminOnly: true)]
    public static void GetRoomOwner(ChatCommandContext ctx)
    {
		if (Core.InnService.GetRoomOwnerFromRoomIn(ctx.Event.SenderCharacterEntity, out var roomOwner))
		{
			if (roomOwner == Entity.Null)
			{
				ctx.Reply("This room is not claimed.");
				return;
			}
			ctx.Reply($"The owner of this room is <color=white>{roomOwner.Read<PlayerCharacter>().Name}</color>.");
		}
		else
		{
			ctx.Reply("This isn't a room within the Inn.");
		}
	}

	[Command("findroom", "fr", description:"creates a spotlight to your door", adminOnly: false)]
    public static void FindMyRoom(ChatCommandContext ctx)
    {
		var door = InnService.GetDoorFromOwner(ctx.Event.SenderCharacterEntity);
		if (door != Entity.Null)
		{
			InnService.AddDoorSpotlight(ctx.Event.SenderCharacterEntity, door);
			ctx.Reply("Follow the beam to your room. Be on the correct floor due to range.");
		}
		else
		{
			ctx.Reply("No door found for your room at the Inn.");
		}
	}

	[Command("dismantleoff", description: "Locks all tiles in a territory that you're on", adminOnly: true)]
    public static void LockTerritory(ChatCommandContext ctx)
    {
        var playerPos = ctx.Event.SenderCharacterEntity.Read<LocalToWorld>().Position;
		int territoryIndex = Core.CastleTerritory.GetTerritoryIndex(playerPos);

		var tiles = Helper.GetAllEntitiesInTerritory<TilePosition>(territoryIndex);
        foreach (var tile in tiles)
        {
            if (tile.Has<EditableTileModel>())
            {
                var etm = tile.Read<EditableTileModel>();
                etm.CanDismantle = false;
                tile.Write(etm);
            }
        }

        ctx.Reply($"Locked {tiles.Count()} tiles in territory {territoryIndex}");
    }

	[Command("moveoff", description: "Prevents tiles from being moved on the territory that you're on", adminOnly: true)]
	public static void MoveLockTerritory(ChatCommandContext ctx)
	{
		var playerPos = ctx.Event.SenderCharacterEntity.Read<LocalToWorld>().Position;
		int territoryIndex = Core.CastleTerritory.GetTerritoryIndex(playerPos);
		var tiles = Helper.GetAllEntitiesInTerritory<TilePosition>(territoryIndex);
		foreach (var tile in tiles)
		{
			if (tile.Has<EditableTileModel>())
			{
				var etm = tile.Read<EditableTileModel>();
				etm.CanMoveAfterBuild = false;
				tile.Write(etm);
			}
		}

		ctx.Reply($"Move locked {tiles.Count()} tiles in territory {territoryIndex}");
	}

	[Command("blockrelocate", "br", description: "Blocks the ability to relocate the castle", adminOnly: true)]
	public static void BlockRelocation(ChatCommandContext ctx)
	{
		var playerPos = ctx.Event.SenderCharacterEntity.Read<LocalToWorld>().Position;
		int territoryIndex = Core.CastleTerritory.GetTerritoryIndex(playerPos);
        var castleHeart = CastleTerritoryService.GetHeartForTerritory(territoryIndex);
		if (castleHeart != Entity.Null)
		{ 
			var castleHeartComponent = castleHeart.Read<CastleHeart>();
			castleHeartComponent.LastRelocationTime = double.PositiveInfinity;
			castleHeart.Write(castleHeartComponent);
			ctx.Reply("Relocation Blocked");
			return;
		}
		ctx.Reply("No castle heart found on this territory.");

	}

	[Command("spawntable", description: "Spawns a chosen research table at mouse location", adminOnly: true)]
	public static void SpawnInnTable(ChatCommandContext ctx, int tableType)
	{
		PrefabGUID prefabGuid = tableType switch
		{
			1 => new PrefabGUID(-495424062),
			2 => new PrefabGUID(-1292809886),
			3 => new PrefabGUID(-1262194203),
			_ => PrefabGUID.Empty
		};

		if (prefabGuid == PrefabGUID.Empty)
		{
			ctx.Reply("Invalid table type. Use .inn spawntable 1, 2, or 3");
			return;
		}

    if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValueWithoutLogging(prefabGuid, out var prefab) &&
        !Core.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(prefabGuid, out prefab))
    {
        ctx.Reply("Tile not found");
        return;
    }

		var spawnPos = ctx.Event.SenderCharacterEntity.Read<EntityAimData>().AimPosition;
		var rot = ctx.Event.SenderCharacterEntity.Read<Rotation>().Value;

		var entity = Core.EntityManager.Instantiate(prefab);
		entity.Write(new Translation { Value = spawnPos });
		entity.Write(new Rotation { Value = rot });

		if (entity.Has<TilePosition>())
		{
			var tilePos = entity.Read<TilePosition>();
			// Get rotation around Y axis
			var euler = new Quaternion(rot.value.x, rot.value.y, rot.value.z, rot.value.w).ToEulerAngles();
			tilePos.TileRotation = (TileRotation)(Mathf.Floor((360 - math.degrees(euler.y) - 45) / 90) % 4);
			entity.Write(tilePos);

			if (entity.Has<StaticTransformCompatible>())
			{
				var stc = entity.Read<StaticTransformCompatible>();
				stc.NonStaticTransform_Rotation = tilePos.TileRotation;
				entity.Write(stc);
			}

			entity.Write(new Rotation { Value = quaternion.RotateY(math.radians(90 * (int)tilePos.TileRotation)) });
		}

		ctx.Reply($"Spawned Research Station Table Type {tableType}");
	}
}


