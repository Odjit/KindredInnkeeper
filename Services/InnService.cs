using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
	static readonly PrefabGUID findContainerSpotlightPrefab = new(-2014639169);
	static readonly PrefabGUID unclaimedDoorSpotlightPrefab = new(-1782768874);

	const float FIND_SPOTLIGHT_DURATION = 15f;

	readonly List<Entity> playersInInn = [];
    Entity innClanEntity = Entity.Null;

    EntityQuery castleHeartQuery;
	EntityQuery innClanQuery;
	EntityQuery roomQuery;
	EntityQuery roomInnQuery;

	readonly Dictionary<Entity, Entity> roomOwners = [];

    public InnService()
    {
		var entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
			.AddAll(new(Il2CppType.Of<CastleHeart>(), ComponentType.AccessMode.ReadWrite))
			.WithOptions(EntityQueryOptions.IncludeDisabled);
		castleHeartQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
		entityQueryBuilder.Dispose();

		entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
			.AddAll(new(Il2CppType.Of<ClanTeam>(), ComponentType.AccessMode.ReadWrite))
			.AddAll(new(Il2CppType.Of<UserOwner>(), ComponentType.AccessMode.ReadWrite))
			.WithOptions(EntityQueryOptions.IncludeDisabled);
		innClanQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
		entityQueryBuilder.Dispose();

		entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
			.AddAll(new(Il2CppType.Of<CastleRoom>(), ComponentType.AccessMode.ReadWrite))
			.AddAll(new(Il2CppType.Of<CastleRoomFloorsBuffer>(), ComponentType.AccessMode.ReadWrite))
			.WithOptions(EntityQueryOptions.IncludeDisabled);
		roomQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
		entityQueryBuilder.Dispose();

		entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp)
			.AddAll(new(Il2CppType.Of<CastleRoom>(), ComponentType.AccessMode.ReadWrite))
			.AddAll(new(Il2CppType.Of<CastleRoomFloorsBuffer>(), ComponentType.AccessMode.ReadWrite))
			.AddAll(new(Il2CppType.Of<UserOwner>(), ComponentType.AccessMode.ReadWrite))
			.WithOptions(EntityQueryOptions.IncludeDisabled);
		roomInnQuery = Core.EntityManager.CreateEntityQuery(ref entityQueryBuilder);
		entityQueryBuilder.Dispose();

		LoadRooms();

		Core.StartCoroutine(CheckPlayersEnteringInn());
		Core.StartCoroutine(CheckPlayersAndRooms());
		AddUnclaimedSpotlights();
	}

	void LoadRooms()
	{
		var innRooms = roomInnQuery.ToEntityArray(Allocator.Temp);
		foreach (var room in innRooms)
		{
			var userOwner = room.Read<UserOwner>().Owner.GetEntityOnServer();
			var character = userOwner!=Entity.Null ?
				userOwner.Read<User>().LocalCharacter.GetEntityOnServer() :
				Entity.Null;
			roomOwners.Add(room, character);
		}
		innRooms.Dispose();
	}

	public Entity GetInnClan()
    {
        if (innClanEntity.Equals(Entity.Null))
        {
            FindInnClan(out innClanEntity);
        }
        return innClanEntity;
    }

    public bool FindInnClan(out Entity clanEntity)
    {
        var clans = innClanQuery.ToEntityArray(Allocator.Temp);

		foreach(var clan in clans)
		{
			clanEntity = clan;
			clans.Dispose();
			return true;
		}
		clans.Dispose();

		clanEntity = Entity.Null;
		return false;
	}

	public bool MakeCurrentClanAnInn(Entity player)
	{
		var teamReference = player.Read<TeamReference>();
		var playerTeam = teamReference.Value;
		var teamAllies = Core.EntityManager.GetBuffer<TeamAllies>(playerTeam);

		foreach (var team in teamAllies)
		{
			if (team.Value.Has<ClanTeam>())
			{
				team.Value.Add<UserOwner>();
				innClanEntity = team.Value;
				return true;
			}
		}

		return false;
	}

	public bool RemoveInnClan()
	{
		var clanEntity = GetInnClan();
		if (clanEntity.Equals(Entity.Null)) return false;

		clanEntity.Remove<UserOwner>();
		innClanEntity = Entity.Null;
		return true;
	}

	public void ClearRoom(Entity roomEntity)
	{
		Core.StartCoroutine(ClearRoom(roomEntity, roomOwners[roomEntity]));
	}

	IEnumerator ClearRoom(Entity roomEntity, Entity roomOwner = default)
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
			var foundHeart = false;
			if (!roomOwner.Equals(Entity.Null))
			{
				var userEntity = roomOwner.Read<PlayerCharacter>().UserEntity;
				// Check if they have a castle heart in which case spawn travel bags for them with their inventory
				var castleHearts = castleHeartQuery.ToEntityArray(Allocator.Temp);
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
				castleHearts.Dispose();
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
			if (!Core.EntityManager.Exists(room)) continue;
			if (!owner.Equals(player)) continue;

			Core.StartCoroutine(ClearRoom(room, player));
			roomOwners[room] = Entity.Null;
			room.Write(new UserOwner() { Owner = Entity.Null });

			AddUnclaimedSpotlightToRoom(room);
			return true;
		}
		return false;
	}

	IEnumerator CheckPlayersAndRooms()
	{
		var wait = new WaitForSeconds(0.05f);
		while (true)
		{
			yield return wait;

			var innClan = GetInnClan();
			if (innClan.Equals(Entity.Null))
				continue;

			// See if anyone left
			var innClanTeamValue = innClan.Read<TeamData>().TeamValue;
			foreach ((var room, var player) in roomOwners)
			{
				if (player.Equals(Entity.Null)) continue;
				if (player.Read<Team>().Value != innClanTeamValue)
				{
					Core.Log.LogInfo($"Player {player.Read<PlayerCharacter>().Name} left the Inn clan, removing their room {room}");
					Core.StartCoroutine(ClearRoom(room, player));
					roomOwners[room] = Entity.Null;
					room.Write(new UserOwner() { Owner = Entity.Null });
					AddUnclaimedSpotlightToRoom(room);
				}
			}

			// See if any rooms were removed
			var roomsToRemove = roomOwners.Keys.Where(x => !Core.EntityManager.Exists(x)).ToArray();
			foreach (var room in roomsToRemove)
				roomOwners.Remove(room);
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
					if (!playersInInn.Contains(userEntity))
                    {
                        playersInInn.Add(userEntity);
                        Buffs.AddBuff(userEntity, charEntity, Prefabs.SetBonus_Silk_Twilight);

						// Check if they aren't in the inn clan
						if (charEntity.Read<Team>().Value != teamValue)
						{
							var message = new FixedString512Bytes("<color=green>Welcome to the Inn!</color> Use <color=yellow>.inn join</color> to join. Inn Info: <color=yellow>.inn info</color>. Complete shelter quests: <color=yellow>.inn quests</color>.");
							ServerChatUtils.SendSystemMessageToClient(Core.EntityManager, user, ref message);
						}
					}
                }
                else
                {
                    if (playersInInn.Contains(userEntity))
                    {
                        playersInInn.Remove(userEntity);
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
			if (floors[0].FloorEntity.GetEntityOnServer().Equals(Entity.Null))
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

				room.Add<UserOwner>();
				room.Write(new UserOwner() { Owner = Entity.Null });

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

				AddUnclaimedSpotlightToRoom(room);
				Core.StartCoroutine(ClearRoom(room, Entity.Null));

				roomOwners.Add(room, Entity.Null);
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
				room.Remove<UserOwner>();
				RemoveUnclaimedSpotlightFromRoom(room);
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
		var innClan = GetInnClan();
		if (innClan.Equals(Entity.Null))
			return RoomSetFailure.NotInInnClan;

		if (player.Read<Team>().Value != innClan.Read<TeamData>().TeamValue)
            return RoomSetFailure.NotInInnClan;
        if (roomOwners.ContainsKey(room))
        {
            roomOwners[room] = player;
			room.Write(new UserOwner() { Owner = player.Read<PlayerCharacter>().UserEntity });

			RemoveUnclaimedSpotlightFromRoom(room);
			Core.StartCoroutine(ClearRoom(room, player));
			return RoomSetFailure.None;
        }
        return RoomSetFailure.RoomDoesNotExist;
    }

    public RoomSetFailure SetRoomOwnerIfEmpty(Entity room, Entity player)
	{
		var innClan = GetInnClan();
		if (innClan.Equals(Entity.Null))
			return RoomSetFailure.NotInInnClan;

		if (player.Read<Team>().Value != innClan.Read<TeamData>().TeamValue)
            return RoomSetFailure.NotInInnClan;
        if (roomOwners.TryGetValue(room, out var roomOwner))
        {
            if (roomOwner.Equals(Entity.Null))
            {
                roomOwners[room] = player;
				room.Write(new UserOwner() { Owner = player.Read<PlayerCharacter>().UserEntity });
				RemoveUnclaimedSpotlightFromRoom(room);
				Core.StartCoroutine(ClearRoom(room, player));
				return RoomSetFailure.None;
            }
            return RoomSetFailure.AlreadyClaimed;
        }
        return RoomSetFailure.RoomDoesNotExist;
    }

	public bool HasClaimedARoom(Entity player) => roomOwners.ContainsValue(player);

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
			if (!Core.EntityManager.Exists(roomChecking)) continue;

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
			if (!Core.EntityManager.Exists(room)) continue;

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

	public static Entity GetDoorFromOwner(Entity owner)
	{
		foreach ((var room, var roomOwner) in Core.InnService.roomOwners)
		{
			if (!Core.EntityManager.Exists(room)) continue;

			if (roomOwner.Equals(owner))
			{
				var walls = Core.EntityManager.GetBuffer<CastleRoomWallsBuffer>(room);
				foreach (var wall in walls)
				{
					if (!wall.WallEntity.GetEntityOnServer().Has<CastleBuildingAttachToParentsBuffer>()) continue;

					var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(wall.WallEntity.GetEntityOnServer());
					foreach (var attachment in attachments)
					{
						var potentialDoor = attachment.ParentEntity.GetEntityOnServer();
						if (potentialDoor.Has<Door>())
						{
							return potentialDoor;
						}
					}
				}
			}
		}
		return Entity.Null;
	}
	readonly Dictionary<Entity, (double expirationTime, List<Entity> targetDoors)> activeSpotlights = [];

	//spotlight a door for a room of the owner
	public static void AddDoorSpotlight(Entity owner, Entity door)
	{
		if (!Core.InnService.activeSpotlights.TryGetValue(owner, out var value))
		{
			value = (Core.ServerTime + 60, new List<Entity>());
			Core.InnService.activeSpotlights.Add(owner, value);
		}
		else
		{
			value.expirationTime = Core.ServerTime + 60;
			value.targetDoors.Add(door);
			Core.InnService.activeSpotlights[owner] = value;
		}
		Buffs.RemoveAndAddBuff(owner, door, findContainerSpotlightPrefab, FIND_SPOTLIGHT_DURATION, UpdateSpotlight);

		void UpdateSpotlight(Entity buffEntity)
		{
			buffEntity.Write<SpellTarget>(new()
			{
				Target = owner
			});
			buffEntity.Write<EntityOwner>(new()
			{
				Owner = owner
			});
			buffEntity.Write<EntityCreator>(new()
			{
				Creator = owner
			});
		}
	}

	public static void ClearSpotlights(Entity owner)
	{
		if (Core.InnService.activeSpotlights.TryGetValue(owner, out var value))
		{
			foreach (var door in value.targetDoors)
			{
				Buffs.RemoveBuff(door, findContainerSpotlightPrefab);
			}
			Core.InnService.activeSpotlights.Remove(owner);
		}
	}

	void AddUnclaimedSpotlights() 
	{
		foreach((var room, var owner) in roomOwners)
		{
			if (owner.Equals(Entity.Null))
			{
				AddUnclaimedSpotlightToRoom(room);
			}
			else
			{
				RemoveUnclaimedSpotlightFromRoom(room);
			}
		}
	}

	static void AddUnclaimedSpotlightToRoom(Entity room)
	{
		var walls = Core.EntityManager.GetBuffer<CastleRoomWallsBuffer>(room);
		foreach (var wall in walls)
		{
			if (!wall.WallEntity.GetEntityOnServer().Has<CastleBuildingAttachToParentsBuffer>()) continue;

			var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(wall.WallEntity.GetEntityOnServer());
			foreach (var attachment in attachments)
			{
				var potentialDoor = attachment.ParentEntity.GetEntityOnServer();
				if (potentialDoor.Has<Door>())
				{
					AddUnclaimedDoorSpotlight(potentialDoor);
				}
			}
		}
	}

	static void RemoveUnclaimedSpotlightFromRoom(Entity room)
	{
		var walls = Core.EntityManager.GetBuffer<CastleRoomWallsBuffer>(room);
		foreach (var wall in walls)
		{
			if (!wall.WallEntity.GetEntityOnServer().Has<CastleBuildingAttachToParentsBuffer>()) continue;

			var attachments = Core.EntityManager.GetBuffer<CastleBuildingAttachToParentsBuffer>(wall.WallEntity.GetEntityOnServer());
			foreach (var attachment in attachments)
			{
				var potentialDoor = attachment.ParentEntity.GetEntityOnServer();
				if (potentialDoor.Has<Door>())
				{
					RemoveClaimedDoorSpotlight(potentialDoor);
				} 
			}
		}
	}

	static void AddUnclaimedDoorSpotlight(Entity door)
	{
		Buffs.RemoveAndAddBuff(door, door, unclaimedDoorSpotlightPrefab, -1f);
	}

	static void RemoveClaimedDoorSpotlight(Entity door)
	{
		Buffs.RemoveBuff(door, unclaimedDoorSpotlightPrefab);
	}
}
