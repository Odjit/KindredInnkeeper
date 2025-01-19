using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using KindredInnkeeper.Data;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace KindredInnkeeper.Services;

internal class InnService
{
    static readonly string CONFIG_PATH = Path.Combine(BepInEx.Paths.ConfigPath, MyPluginInfo.PLUGIN_NAME);
    static readonly string ROOMS_PATH = Path.Combine(CONFIG_PATH, "rooms.json");

    readonly List<Entity> playersInInn = [];
    Entity innClanEntity = Entity.Null;

    EntityQuery castleHeartQuery;
    EntityQuery roomQuery;

    readonly Dictionary<Entity, Entity> roomOwners = [];

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
		Core.StartCoroutine(CheckPlayersLeavingClan());
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

	IEnumerator HandlePlayerLeavingRoom(Entity charEntity, Entity roomEntity)
	{
		List<Entity> inventoryEntities = [];

		// See if they had any inventory in the room
		// Clear any research
		var floors = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(roomEntity);
		foreach (var floor in floors)
		{
			var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(floor.FloorEntity.GetEntityOnServer());
			foreach (var attachment in attachments)
			{
				var attachmentEntity = attachment.ParentEntity.GetEntityOnServer();
				if (attachmentEntity.Has<AttachedBuffer>())
				{
					var attachedBuffer = Core.EntityManager.GetBuffer<AttachedBuffer>(attachmentEntity);
					foreach (var potentialExternalInventory in attachedBuffer)
					{
						if (!potentialExternalInventory.PrefabGuid.Equals(Prefabs.External_Inventory)) continue;

						// Check if the inventory has anything in it
						var externalInventory = potentialExternalInventory.Entity;
						var inventory = externalInventory.ReadBuffer<InventoryBuffer>();
						foreach (var item in inventory)
						{
							if (item.Amount >= 1)
							{
								inventoryEntities.Add(externalInventory);
								break;
							}
						}
					}
				}

				if (attachmentEntity.Has<ResearchBuffer>())
				{
					var researchBuffer = Core.EntityManager.GetBuffer<ResearchBuffer>(attachmentEntity);
					for (var i = 0; i<researchBuffer.Length; ++i)
					{
						var research = researchBuffer[i];
						research.IsResearchByStation = false;
						researchBuffer[i] = research;
					}
				}

				if (attachmentEntity.Has<RespawnPoint>())
				{
					var researchPoint = attachmentEntity.Read<RespawnPoint>();
					researchPoint.HasRespawnPointOwner = false;
					researchPoint.RespawnPointOwner = Entity.Null;
					attachmentEntity.Write(researchPoint);
				}

				if (attachmentEntity.Has<Residency>())
				{
					var residency = attachmentEntity.Read<Residency>();
					if (!residency.Resident.Equals(Entity.Null))
					{
						Buffs.RemoveBuff(residency.Resident, residency.InsideBuff);
					}
				}
			}
		}

		if (inventoryEntities.Count > 0)
		{
			var userEntity = charEntity.Read<PlayerCharacter>().UserEntity;
			// Check if they have a castle heart in which case spawn travel bags for them with their inventory
			var castleHearts = castleHeartQuery.ToEntityArray(Allocator.Temp);
			var foundHeart = false;
			foreach (var castleHeartEntity in castleHearts)
			{
				var userOwner = castleHeartEntity.Read<UserOwner>();
				if (!userOwner.Owner.GetEntityOnServer().Equals(userEntity)) continue;

				// Spawn travel bags
				var prefabCollection = Core.Server.GetExistingSystemManaged<PrefabCollectionSystem>();
				prefabCollection._PrefabLookupMap.TryGetValue(Prefabs.TM_Stash_Chest_Rebuilding, out var travelBagsPrefab);
				var travelBagsEntity = Core.EntityManager.Instantiate(travelBagsPrefab);

				var offset = new float3(1.5f, 0f, 3.25f);
				// Should figure out a better location for the tile position but for now it is hardcoded
				var tilePosition = castleHeartEntity.Read<TilePosition>();
				tilePosition.Tile += new int2(2, 2);
				travelBagsEntity.Write(tilePosition);

				var translation = castleHeartEntity.Read<Translation>();
				translation.Value += offset;
				travelBagsEntity.Write(translation);

				var localTransform = castleHeartEntity.Read<LocalTransform>();
				localTransform.Position += offset;
				travelBagsEntity.Write(localTransform);

				var rotation = castleHeartEntity.Read<Rotation>();
				travelBagsEntity.Write(rotation);

				travelBagsEntity.Write(userOwner);
				travelBagsEntity.Write(new CastleHeartConnection() { CastleHeartEntity = castleHeartEntity });
				travelBagsEntity.Write(castleHeartEntity.Read<TeamReference>());

				yield return null;

				// Put inventory into travel bags
				var inventoryInstanceElements = Core.EntityManager.GetBuffer<InventoryInstanceElement>(travelBagsEntity);
				var externalInventoryEntity = inventoryInstanceElements[0].ExternalInventoryEntity.GetEntityOnServer();

				var travelBagInventoryBuffer = Core.EntityManager.GetBuffer<InventoryBuffer>(externalInventoryEntity);
				travelBagInventoryBuffer.Clear();
				foreach (var inventoryEntity in inventoryEntities)
				{
					var inventory = inventoryEntity.ReadBuffer<InventoryBuffer>();
					for (var i = 0; i < inventory.Length; ++i)
					{
						var item = inventory[i];
						if (item.Amount >= 1)
						{
							travelBagInventoryBuffer.Add(item);
							inventory[i] = InventoryBuffer.Empty();
						}
					}
				}

				foundHeart = true;
				break;
			}

			if (!foundHeart)
			{
				// Clear all the inventory left behind
				foreach (var inventoryEntity in inventoryEntities)
				{
					var inventory = inventoryEntity.ReadBuffer<InventoryBuffer>();
					for (var i = 0; i < inventory.Length; ++i)
					{
						inventory[i] = InventoryBuffer.Empty();
					}
				}
			}
		}
	}

	public bool LeaveRoom(Entity player)
	{
		foreach ((var room, var owner) in roomOwners)
		{
			if (!owner.Equals(player)) continue;

			Core.StartCoroutine(HandlePlayerLeavingRoom(player, room));
			roomOwners[room] = Entity.Null;
			SaveRooms();
			return true;
		}
		return false;
	}

	IEnumerator CheckPlayersLeavingClan()
	{
		var wait = new WaitForSeconds(0.05f);
		while (true)
		{
			yield return wait;

			var innClan = GetInnClan();
			var innClanTeamValue = innClan.Read<TeamData>().TeamValue;
			var change = false;
			foreach ((var room, var player) in roomOwners)
			{
				if (player.Equals(Entity.Null)) continue;
				if (player.Read<Team>().Value != innClanTeamValue)
				{
					Core.Log.LogInfo($"Player {player.Read<PlayerCharacter>().Name} left the Inn clan, removing their room {room}");
					Core.StartCoroutine(HandlePlayerLeavingRoom(player, room));
					change = true;
					roomOwners[room] = Entity.Null;
				}
			}

			if (change)
				SaveRooms();
		}
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

				// Make it so Logistics won't use any of the inventory in the room
				var floorsInRoom = Core.EntityManager.GetBuffer<CastleRoomFloorsBuffer>(room);
				foreach (var floor in floorsInRoom)
				{
					var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(floor.FloorEntity.GetEntityOnServer());
					foreach (var attachment in attachments)
					{
						var parentEntity = attachment.ParentEntity.GetEntityOnServer();
						if (parentEntity.Has<NameableInteractable>())
						{
							var nameable = parentEntity.Read<NameableInteractable>();

							if (nameable.Name.IsEmpty)
							{
								var prefabGuid = parentEntity.Read<PrefabGUID>();
								var prefabName = Core.Localization.GetPrefabName(prefabGuid);
								nameable.Name = prefabName;
							}

							if (!nameable.Name.ToString().Contains("''"))
							{
								if (nameable.Name.Length < 61)
								{
									nameable.Name += "''";
								}
								else
								{
									// Truncate and append
									nameable.Name = nameable.Name.ToString()[..61] + "''";
								}
								parentEntity.Write(nameable);
							}
						}
					}
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

	public bool RemoveRoomFromInn(Entity player)
	{
		if (GetRoomIn(player, out var room))
		{
			if (roomOwners.Remove(room))
			{
				SaveRooms();
				return true;
			}
		}
		return false;
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

	public bool HasClaimedARoom(Entity player) => roomOwners.ContainsValue(player);

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

    public IEnumerable<Entity> GetRoomOwners() => roomOwners.Values.Where(x => !x.Equals(Entity.Null));

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
