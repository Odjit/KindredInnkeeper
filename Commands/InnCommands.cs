using KindredInnkeeper.Commands.Converters;
using KindredInnkeeper.Services;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
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

        TeamUtility.AddUserToClan(Core.EntityManager, clanEntity, userToAddEntity, ref user);
        userToAddEntity.Write<User>(user);

        var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clanEntity);
        var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clanEntity);

        for (var i = 0; i < members.Length; ++i)
        {
            var member = members[i];
            var userBufferEntry = userBuffer[i];
            var userToTest = userBufferEntry.UserEntity.Read<User>();
            if (userToTest.CharacterName.Equals(user.CharacterName))
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
        rules.AppendLine("<color=yellow>Welcome to the</color> <color=lightblue>Inn</color><color=yellow>!</color> ");
        rules.AppendLine("<color=green>1.</color> This is temporary stay. Please find other accomodations after 1 week.");
        rules.AppendLine("<color=green>2.</color> No stealing from other players. Stay out of other's rooms.");
        rules.AppendLine("<color=green>3.</color> Claim a room by renaming the chest outside it.");
        rules.AppendLine("<color=green>4.</color> Leave the clan once you find a plot.");
        rules.AppendLine("<color=green>5.</color> Rename the chest back to 'RenameToClaim' when leaving.");
        ctx.Reply(rules.ToString());
    }

    [Command("guests", description: "Displays the guests of the Inn", adminOnly: true)]
    public static void DisplayGuests(ChatCommandContext ctx)
    {
        var clanEntity = Core.InnService.GetInnClan();
        if (clanEntity == Entity.Null)
        {
            ctx.Reply("No clan found matching name 'Inn' with a leader of 'InnKeeper'");
            return;
        }

        var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clanEntity);
        var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clanEntity);

        var guests = new StringBuilder();
        for (var i = 0; i < members.Length; ++i)
        {
            var member = members[i];
            var userBufferEntry = userBuffer[i];
            var user = userBufferEntry.UserEntity.Read<User>();
            if (member.ClanRole == ClanRoleEnum.Member)
            {
                var guestName = user.CharacterName;
                guests.AppendLine($"<color=white>{guestName}</color>");
            }
        }

        ctx.Reply(guests.ToString());
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

    [Command("addroom", description: "Adds a room to the Inn", adminOnly: true)]
    public static void AddRoom(ChatCommandContext ctx)
    {
        var result = Core.InnService.AddRoomToInn(ctx.Event.SenderCharacterEntity);
        if (result != "")
        {
            ctx.Reply("Failed to add a room to the Inn: "+result);
            return;
        }
        ctx.Reply("Added a room to the Inn.");
    }


    [Command("listrooms", description: "Lists the rooms in the Inn", adminOnly: true)]
    public static void ListRooms(ChatCommandContext ctx)
    {
        var roomOwners = Core.InnService.GetRoomOwners();

        ctx.Reply($"{Core.InnService.GetFreeRoomCount()} free rooms out of {Core.InnService.GetRoomCount()} rooms in the inn.\n" +
            string.Join(", ", roomOwners) + " have rooms in the inn.");
    }

    [Command("claimroom")]
    public static void ClaimRoom(ChatCommandContext ctx)
    {
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
        if (Core.InnService.GetRoomIn(ctx.Event.SenderCharacterEntity, out var roomIn))
        {
            var owner = Core.InnService.GetRoomOwnerFromRoomIn(roomIn, out var roomOwner);
            if (owner)
            {
                ctx.Reply($"The owner of this room is <color=white>{roomOwner.Read<PlayerCharacter>().Name}</white>.");
            }
            else
            {
                ctx.Reply("This room is not claimed.");
            }
        }
    }
}
