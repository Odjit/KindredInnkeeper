using ProjectM;
using ProjectM.Network;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace KindredInnkeeper.Patches;

[HarmonyLib.HarmonyPatch(typeof(PlaceTileModelSystem), nameof(PlaceTileModelSystem.OnUpdate))]
static class PlaceTileModelSystemPatch
{
    readonly static Dictionary<Entity, double> lastBuildCastleHeart = [];

    static void Prefix(PlaceTileModelSystem __instance)
    {
        if (Core.InnService == null) return;

        var clanEntity = Core.InnService.GetInnClan();
        if (clanEntity.Equals(Entity.Null)) return;
        var clanTeamValue = clanEntity.Read<TeamData>().TeamValue;

        var buildEvents = __instance._BuildTileQuery.ToEntityArray(Allocator.Temp);
        foreach (var buildEvent in buildEvents)
        {
            var fromCharacter = buildEvent.Read<FromCharacter>();
			var btme = buildEvent.Read<BuildTileModelEvent>();

            var playerTeamValue = fromCharacter.User.Read<Team>().Value;
            if (playerTeamValue != clanTeamValue) continue;

			var clanRole = fromCharacter.User.Read<ClanRole>();
			if (clanRole.Value == ClanRoleEnum.Leader) continue;

			
            if (btme.PrefabGuid == Data.Prefabs.TM_BloodFountain_CastleHeart)
            {
                if(!lastBuildCastleHeart.TryGetValue(fromCharacter.Character, out var lastBuildTime) || 
                   lastBuildTime + 60 < Core.ServerTime)
                {
					var message = new FixedString512Bytes("If you want to build a castle heart make note you will be removed from the inn.  Try building another one within a minute to actually place one.");
					ServerChatUtils.SendSystemMessageToClient(Core.EntityManager,
                    fromCharacter.User.Read<User>(),
                    ref message);

                    if (lastBuildCastleHeart.ContainsKey(fromCharacter.Character))
                    {
                        lastBuildCastleHeart[fromCharacter.Character] = Core.ServerTime;
                    }
                    else
                    {
                        lastBuildCastleHeart.Add(fromCharacter.Character, Core.ServerTime);
                    }

                    Core.EntityManager.DestroyEntity(buildEvent);
                    continue;
                }

                var playerToRemove = fromCharacter.User;
                var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clanEntity);
                var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clanEntity);
                bool foundLeader = false;
                FromCharacter fromCharacterForLeader = new();
                for (var i = 0; i < members.Length; ++i)
                {
                    var member = members[i];
                    if (member.ClanRole == ClanRoleEnum.Leader)
                    {
                        var userBufferEntry = userBuffer[i];
                        fromCharacterForLeader = new FromCharacter()
                        {
                            Character = userBufferEntry.UserEntity.Read<User>().LocalCharacter.GetEntityOnServer(),
                            User = userBufferEntry.UserEntity
                        };
                        foundLeader = true;
                        break;
                    }
                }

                if (!foundLeader)
                {
                    continue;
                }

                for (var i = 0; i < members.Length; ++i)
                {
                    var userBufferEntry = userBuffer[i];
                    if (userBufferEntry.UserEntity.Equals(playerToRemove))
                    {
                        var member = members[i];
                        if (member.ClanRole == ClanRoleEnum.Leader)
                        {
                            continue;
                        }


						var archetype = Core.EntityManager.CreateArchetype(new ComponentType[] {
							ComponentType.ReadWrite<FromCharacter>(),
							ComponentType.ReadWrite<ClanEvents_Client.Kick_Request>()
						});

						var entity = Core.EntityManager.CreateEntity(archetype);
                        entity.Write(fromCharacterForLeader);

                        entity.Write(new ClanEvents_Client.Kick_Request()
                        {
                            TargetUserIndex = members[i].UserIndex
                        });

						var message = new FixedString512Bytes("Removing you from the Inn and good luck on your castle!");
                        ServerChatUtils.SendSystemMessageToClient(Core.EntityManager,
                        fromCharacter.User.Read<User>(),
                        ref message);
                    }
                }
            }
            else
            {
				var message = new FixedString512Bytes("Can't build anything besides a castle heart while a member of the Inn.  Note building a castle heart will kick you.");
                ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, 
                    fromCharacter.Character.Read<PlayerCharacter>().UserEntity.Read<User>(),
                    ref message);
                Core.EntityManager.DestroyEntity(buildEvent);
            }
        }

		buildEvents.Dispose();

		var wallpaperEvents = __instance._BuildWallpaperQuery.ToEntityArray(Allocator.Temp);
		foreach (var wallpaperEvent in wallpaperEvents)
		{
			var fromCharacter = wallpaperEvent.Read<FromCharacter>();

			var playerTeamValue = fromCharacter.User.Read<Team>().Value;
			if (playerTeamValue != clanTeamValue) continue;

			var clanRole = fromCharacter.User.Read<ClanRole>();
			if (clanRole.Value == ClanRoleEnum.Leader) continue;
			
			Core.EntityManager.DestroyEntity(wallpaperEvent);
		}
		wallpaperEvents.Dispose();

		var tileModelVariationEvents = __instance._SetVariationQuery.ToEntityArray(Allocator.Temp);
		foreach (var tileModelVariationEvent in tileModelVariationEvents)
		{
			var fromCharacter = tileModelVariationEvent.Read<FromCharacter>();

			var playerTeamValue = fromCharacter.User.Read<Team>().Value;
			if (playerTeamValue != clanTeamValue) continue;

			var clanRole = fromCharacter.User.Read<ClanRole>();
			if (clanRole.Value == ClanRoleEnum.Leader) continue;

			Core.EntityManager.DestroyEntity(tileModelVariationEvent);
		}
		tileModelVariationEvents.Dispose();
	}
}
