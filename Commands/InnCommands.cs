using KindredInnkeeper.Commands.Converters;
using KindredInnkeeper.Services;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Entities;
using VampireCommandFramework;


namespace KindredInnkeeper.Commands;

[CommandGroup("inn")]
internal class InnCommands
{
    [Command("enter", "join", description: "Adds self to a clan of the name Inn that has a leader by the name of InnKeeper", adminOnly: false)]
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
            ctx.Reply($"No clan found matching name 'Inn' with a leader of 'InnKeeper'");
            return;
        }

		userToAddEntity.Write(new ClanRole() { Value = ClanRoleEnum.Member });

		TeamUtility.AddUserToClan(Core.EntityManager, clanEntity, userToAddEntity, ref user);
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

    [Command("rules", description: "Displays the rules of the Inn", adminOnly: false)]
    public static void DisplayRules(ChatCommandContext ctx)
    {
        var rules = new StringBuilder();
        rules.AppendLine("<color=yellow>Welcome to the Inn!</color>");
        rules.AppendLine("<color=green>1.</color> This is temporary stay. Please find other accomodations asap.");
        rules.AppendLine("<color=green>2.</color> Doors report their guest. Use '.inn vacancy' to see availablility.");
        rules.AppendLine("<color=green>3.</color> Claim a room: Type '.inn claimroom' in chat while in the room.");
        rules.AppendLine("<color=green>4.</color> Claiming a plot kicks you from the Inn. Your storage will follow.");
        rules.AppendLine("<color=green>5.</color> Leaving the clan will forfeit any items left in your room.");
		ctx.Reply(rules.ToString());
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
        ctx.Reply("You are not standing in a room.");
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
				ctx.Reply("You haven't claimed a room yet.");
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
        var roomOwners = Core.InnService.GetRoomOwners();
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
        ctx.Reply("You are not standing in a room.");
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
}
