using Il2CppInterop.Runtime;
using KindredInnkeeper.Data;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace KindredInnkeeper.Services;

internal class InnService
{
    static readonly string CONFIG_PATH = Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
    static readonly string ROOMS_PATH = Path.Combine(CONFIG_PATH, "rooms.json");

    List<Entity> playersInInn = [];
    Entity innClanEntity = Entity.Null;

    EntityQuery castleHeartQuery;
    EntityQuery roomQuery;

    Dictionary<Entity, Entity> roomOwners = [];

    public InnService()
    {
        EntityQueryDesc queryDesc = new()
        {
            All = new ComponentType[] { new(Il2CppType.Of<CastleHeart>(), ComponentType.AccessMode.ReadWrite) },
            Options = EntityQueryOptions.IncludeDisabled
        };

        castleHeartQuery = Core.EntityManager.CreateEntityQuery(queryDesc);

        queryDesc = new EntityQueryDesc
        {
            All = new ComponentType[] { new(Il2CppType.Of<CastleRoom>()), new(Il2CppType.Of<CastleRoomFloorsBuffer>()) },
            Options = EntityQueryOptions.IncludeDisabled
        };
        roomQuery = Core.EntityManager.CreateEntityQuery(queryDesc);

        LoadRooms();

        Core.StartCoroutine(CheckPlayersEnteringInn());
    }

    public void FinishedLoading()
    {
        SaveRooms();
    }

    void SaveRooms()
    {
        var saving = new Dictionary<string, string>();
        foreach((var room, var owner) in roomOwners)
        {
            var n = room.Read<NetworkId>();
            var roomNetworkId = (n.Normal_Index, n.Normal_Generation);
            var ownerNetworkId = (-1, -1);
            if (!owner.Equals(Entity.Null))
            {
                n = owner.Read<NetworkId>();
                ownerNetworkId = (n.Normal_Index, n.Normal_Generation);
            }
            saving.Add(roomNetworkId.ToString(), ownerNetworkId.ToString());
        }


        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(saving, options);
        File.WriteAllText(ROOMS_PATH, json);
    }

    void LoadRooms()
    {
        if (!File.Exists(ROOMS_PATH))
        {
            return;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        var json = File.ReadAllText(ROOMS_PATH);
        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);

        var rooms = Helper.GetEntitiesByComponentTypes<CastleRoom, NetworkId>(includeDisabled: true);
        var roomNetworkIds = rooms.ToArray()
            .ToDictionary(x => (x.Read<NetworkId>().Normal_Index, (int)x.Read<NetworkId>().Normal_Generation).ToString(),
                            x => x);
        rooms.Dispose();

        var players = Helper.GetEntitiesByComponentTypes<PlayerCharacter, NetworkId>(includeDisabled: true);
        var playersNetworkIds = players.ToArray()
            .ToDictionary(x => (x.Read<NetworkId>().Normal_Index, (int)x.Read<NetworkId>().Normal_Generation).ToString(),
                                            x => x);
        playersNetworkIds.Add((-1, -1).ToString(), Entity.Null);
        players.Dispose();


        foreach ((var roomNetworkId, var ownerNetworkId) in loaded)
        {
            var room = roomNetworkIds[roomNetworkId];
            var owner = playersNetworkIds[ownerNetworkId];
            roomOwners.Add(room, owner);
        }
    }

    public Entity GetInnClan()
    {
        if (innClanEntity.Equals(Entity.Null))
        {
            FindClanLead("Inn", "InnKeeper", out innClanEntity);
        }
        return innClanEntity;
    }

    public static bool FindClanLead(string clanName, string leaderName, out Entity clanEntity)
    {
        var clans = Helper.GetEntitiesByComponentType<ClanTeam>().ToArray();
        var matchedClans = clans.Where(x => x.Read<ClanTeam>().Name.ToString().ToLower() == clanName.ToLower());

        foreach (var clan in matchedClans)
        {
            var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clan);
            if (members.Length == 0) continue;
            var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clan);
            for (var i = 0; i < members.Length; ++i)
            {
                var member = members[i];
                var userBufferEntry = userBuffer[i];
                var user = userBufferEntry.UserEntity.Read<User>();
                if (user.CharacterName.ToString().ToLower() == leaderName.ToLower())
                {
                    clanEntity = clan;
                    return true;
                }
            }
        }
        clanEntity = Entity.Null;
        return false;
    }

    public static Entity GetClanLeader(string clanName, string leaderName)
    {
        var clans = Helper.GetEntitiesByComponentType<ClanTeam>().ToArray();
        var matchedClans = clans.Where(x => x.Read<ClanTeam>().Name.ToString().ToLower() == clanName.ToLower());

        foreach (var clan in matchedClans)
        {
            var members = Core.EntityManager.GetBuffer<ClanMemberStatus>(clan);
            if (members.Length == 0) continue;
            var userBuffer = Core.EntityManager.GetBuffer<SyncToUserBuffer>(clan);
            for (var i = 0; i < members.Length; ++i)
            {
                var member = members[i];
                var userBufferEntry = userBuffer[i];
                var user = userBufferEntry.UserEntity.Read<User>();
                if (user.CharacterName.ToString().ToLower() == leaderName.ToLower())
                {
                    return userBufferEntry.UserEntity;
                }
            }
        }
        return Entity.Null;


    }

    IEnumerator CheckPlayersEnteringInn()
    {
        var innTerritories = new List<int>();
        var wait = new WaitForSeconds(2.5f);
        while (true)
        {
            yield return wait;
                
            var innClan = GetInnClan();
            if (innClan.Equals(Entity.Null))
                continue;

            var teamValue = innClan.Read<TeamData>().TeamValue;

            innTerritories.Clear();
            var castleHearts = castleHeartQuery.ToEntityArray(Allocator.Temp);
            foreach(var castleHeartEntity in castleHearts)
            {
                var heartTeam = castleHeartEntity.Read<Team>();
                if (heartTeam.Value != teamValue)
                    continue;

                var pos = castleHeartEntity.Read<LocalToWorld>().Position;
                var territoryIndex = Core.CastleTerritory.GetTerritoryIndex(pos);
                if (territoryIndex != -1)
                    innTerritories.Add(territoryIndex);
            }
            castleHearts.Dispose();

            foreach (var userEntity in Core.Players.GetCachedUsersOnline())
            {
                var user = userEntity.Read<User>();
                var charEntity = user.LocalCharacter.GetEntityOnServer();
                var pos = charEntity.Read<LocalToWorld>().Position;
                var territoryIndex = Core.CastleTerritory.GetTerritoryIndex(pos);
                if (innTerritories.Contains(territoryIndex))
                {
                    var territoryEntity = Core.CastleTerritory.GetHeartForTerritory(territoryIndex);
                    var userOwner = territoryEntity.Read<UserOwner>();
                    var clanLeader = GetClanLeader("Inn", "InnKeeper");
                    if (!userOwner.Owner.GetEntityOnServer().Equals(clanLeader))
                        continue;

                    if (!playersInInn.Contains(userEntity))
                    {
                        playersInInn.Add(userEntity);
                        ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, user, "<color=green>Welcome to the Inn!</color> Use <color=yellow>.inn enter</color> to join. Rules of the Inn: <color=yellow>.inn rules</color>. Complete shelter quests: <color=yellow>.inn quests</color>.");
                        Buffs.AddBuff(userEntity, charEntity, Prefabs.AB_Interact_Curse_Wisp_Buff, -1);
                        Buffs.AddBuff(userEntity, charEntity, Prefabs.SetBonus_Silk_Twilight);
                    }
                }
                else
                {
                    if (playersInInn.Contains(userEntity))
                    {
                        playersInInn.Remove(userEntity);
                        Buffs.RemoveBuff(charEntity, Prefabs.AB_Interact_Curse_Wisp_Buff);
                        Buffs.RemoveBuff(charEntity, Prefabs.SetBonus_Silk_Twilight);

                    }
                }

            }
        }
    }

    public string AddRoomToInn(Entity player)
    {
        var clanTeam = GetInnClan();
        if (clanTeam.Equals(Entity.Null))
            return "There is no Inn clan";

        var playerTilePos = player.Read<TilePosition>().Tile;
        var playerHeight = player.Read<Height>().Value;

        var foundRoom = false;
        var teamValue = clanTeam.Read<TeamData>().TeamValue;
        var rooms = roomQuery.ToEntityArray(Allocator.Temp);
        foreach (var room in rooms)
        {
            var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(room);
            if (floors.Length == 0)
                continue;
            if (floors[0].FloorEntity.GetEntityOnServer().Read<Team>().Value != teamValue)
                continue;
            foreach (var floor in floors)
            {
                var tileBounds = floor.FloorEntity.GetEntityOnServer().Read<TileBounds>().Value;
                if (!tileBounds.Contains(playerTilePos)) continue;

                var height = floor.FloorEntity.GetEntityOnServer().Read<StaticTransformCompatible>().NonStaticTransform_Height;
                if (Mathf.Abs(playerHeight - height) < 0.1f)
                {
                    foundRoom = true;
                    break;
                }
            }

            if (foundRoom)
            {
                if (roomOwners.ContainsKey(room))
                {
                    return "Room has already been added to the Inn";
                }

                roomOwners.Add(room, Entity.Null);
                SaveRooms();
                Core.Log.LogInfo($"Room added to Inn {room.Index}:{room.Version}");
                break;
            }
        }
        rooms.Dispose();

        if (!foundRoom)
            return "You are not in a room in the Inn";
        return "";
    }

    public enum RoomSetFailure
    {
        None,
        RoomDoesNotExist,
        NotInInnClan,
        AlreadyClaimed
    }

    public RoomSetFailure SetRoomOwner(Entity room, Entity player)
    {
        if(player.Read<Team>().Value != GetInnClan().Read<TeamData>().TeamValue)
            return RoomSetFailure.NotInInnClan;
        if (roomOwners.ContainsKey(room))
        {
            roomOwners[room] = player;
            SaveRooms();
            return RoomSetFailure.None;
        }
        return RoomSetFailure.RoomDoesNotExist;
    }

    public RoomSetFailure SetRoomOwnerIfEmpty(Entity room, Entity player)
    {
        if (player.Read<Team>().Value != GetInnClan().Read<TeamData>().TeamValue)
            return RoomSetFailure.NotInInnClan;
        if (roomOwners.TryGetValue(room, out var roomOwner))
        {
            if (roomOwner.Equals(Entity.Null))
            {
                roomOwners[room] = player;
                SaveRooms();
                return RoomSetFailure.None;
            }
            return RoomSetFailure.AlreadyClaimed;
        }
        return RoomSetFailure.RoomDoesNotExist;
    }

    public void RemoveRoomOwner(Entity room) { roomOwners.Remove(room); }

    public bool GetRoomOwner(Entity room, out Entity roomOwner)
    {
        return roomOwners.TryGetValue(room, out roomOwner);
    }

    public bool GetRoomIn(Entity entity, out Entity room)
    {
        var tilePosition = entity.Read<TilePosition>().Tile;
        var height = entity.Read<Height>().Value;

        foreach(var roomChecking in roomOwners.Keys)
        {
            var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(roomChecking);
            foreach (var floor in floors)
            {
                var floorEntity = floor.FloorEntity.GetEntityOnServer();
                var tileBounds = floorEntity.Read<TileBounds>().Value;
                if (!tileBounds.Contains(tilePosition)) continue;

                var floorHeight = floorEntity.Read<StaticTransformCompatible>().NonStaticTransform_Height;
                if (Mathf.Abs(height - floorHeight) < 0.1f)
                {
                    room = roomChecking;
                    return true;
                }
            }
        }

        room = Entity.Null;
        return false;
    }

    public IEnumerator<Entity> GetRoomOwners() => roomOwners.Values.GetEnumerator();

    public int GetRoomCount() => roomOwners.Count;

    public int GetFreeRoomCount() => roomOwners.Count(x => x.Value.Equals(Entity.Null));

    public bool GetRoomOwnerFromRoomIn(Entity entity, out Entity roomOwner)
    {
        if (GetRoomIn(entity, out var room))
        {
            return GetRoomOwner(room, out roomOwner);
        }
        roomOwner = Entity.Null;
        return false;
    }

    public bool GetRoomOwnerForNetworkId(NetworkId networkId, out Entity roomOwner)
    {
        foreach ((var room, var owner) in roomOwners)
        {
            var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(room);
            foreach (var floor in floors)
            {
                var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(floor.FloorEntity.GetEntityOnServer());
                foreach (var attachment in attachments)
                {
                    var potentialEntity = attachment.ParentEntity.GetEntityOnServer();
                    if (potentialEntity.Has<NetworkId>() && potentialEntity.Read<NetworkId>() == networkId)
                    {
                        roomOwner = owner;
                        return true;
                    }
                }
            }
        }

        roomOwner = Entity.Null;
        return false;
    }
}
