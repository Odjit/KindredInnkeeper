using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppSystem;
using KindredInnkeeper.Data;
using ProjectM;
using ProjectM.Gameplay.Clan;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VampireCommandFramework;
namespace KindredInnkeeper;

// This is an anti-pattern, move stuff away from Helper not into it
internal static partial class Helper
{
	public static AdminAuthSystem adminAuthSystem = Core.Server.GetExistingSystemManaged<AdminAuthSystem>();
	public static ClanSystem_Server clanSystem = Core.Server.GetExistingSystemManaged<ClanSystem_Server>();
	public static EntityCommandBufferSystem entityCommandBufferSystem = Core.Server.GetExistingSystemManaged<EntityCommandBufferSystem>();

	public static PrefabGUID GetPrefabGUID(Entity entity)
	{
		var entityManager = Core.EntityManager;
		PrefabGUID guid;
		try
		{
			guid = entityManager.GetComponentData<PrefabGUID>(entity);
		}
		catch
		{
			guid = new PrefabGUID(0);
		}
		return guid;
	}

	public static bool TryGetClanEntityFromPlayer(Entity User, out Entity ClanEntity)
	{
		if (User.Read<TeamReference>().Value._Value.ReadBuffer<TeamAllies>().Length > 0)
		{
			ClanEntity = User.Read<TeamReference>().Value._Value.ReadBuffer<TeamAllies>()[0].Value;
			return true;
		}
		ClanEntity = new Entity();
		return false;
	}

	public static Entity AddItemToInventory(Entity recipient, PrefabGUID guid, int amount)
	{
		try
		{
			ServerGameManager serverGameManager = Core.Server.GetExistingSystemManaged<ServerScriptMapper>()._ServerGameManager;
			var inventoryResponse = serverGameManager.TryAddInventoryItem(recipient, guid, amount);

			return inventoryResponse.NewEntity;
		}
		catch (System.Exception e)
		{
			Core.LogException(e);
		}
		return new Entity();
	}

	public static NativeArray<Entity> GetEntitiesByComponentType<T1>(bool includeAll = false, bool includeDisabled = false, bool includeSpawn = false, bool includePrefab = false, bool includeDestroyed = false)
	{
		EntityQueryOptions options = EntityQueryOptions.Default;
		if (includeAll) options |= EntityQueryOptions.IncludeAll;
		if (includeDisabled) options |= EntityQueryOptions.IncludeDisabled;
		if (includeSpawn) options |= EntityQueryOptions.IncludeSpawnTag;
		if (includePrefab) options |= EntityQueryOptions.IncludePrefab;
		if (includeDestroyed) options |= EntityQueryOptions.IncludeDestroyTag;

		EntityQueryDesc queryDesc = new()
		{
			All = new ComponentType[] { new(Il2CppType.Of<T1>(), ComponentType.AccessMode.ReadWrite) },
			Options = options
		};

		var query = Core.EntityManager.CreateEntityQuery(queryDesc);

		var entities = query.ToEntityArray(Allocator.Temp);
		return entities;
	}

	public static NativeArray<Entity> GetEntitiesByComponentTypes<T1, T2>(bool includeAll = false, bool includeDisabled = false, bool includeSpawn = false, bool includePrefab = false, bool includeDestroyed = false)
	{
		EntityQueryOptions options = EntityQueryOptions.Default;
		if (includeAll) options |= EntityQueryOptions.IncludeAll;
		if (includeDisabled) options |= EntityQueryOptions.IncludeDisabled;
		if (includeSpawn) options |= EntityQueryOptions.IncludeSpawnTag;
		if (includePrefab) options |= EntityQueryOptions.IncludePrefab;
		if (includeDestroyed) options |= EntityQueryOptions.IncludeDestroyTag;

		EntityQueryDesc queryDesc = new()
		{
			All = new ComponentType[] { new(Il2CppType.Of<T1>(), ComponentType.AccessMode.ReadWrite), new(Il2CppType.Of<T2>(), ComponentType.AccessMode.ReadWrite) },
			Options = options
		};

		var query = Core.EntityManager.CreateEntityQuery(queryDesc);

		var entities = query.ToEntityArray(Allocator.Temp);
		return entities;
	}

	public static IEnumerable<Entity> GetAllEntitiesInRadius<T>(float2 center, float radius)
	{
		var entities = GetEntitiesByComponentType<T>(includeSpawn: true, includeDisabled: true);
		foreach (var entity in entities)
		{
			if (!entity.Has<Translation>()) continue;
			var pos = entity.Read<Translation>().Value;
			if (math.distance(center, pos.xz) <= radius)
			{
				yield return entity;
			}
		}
		entities.Dispose();
	}

	static EntityQuery tilePositionQuery = default;
	public static Entity FindClosestTilePosition(Vector3 pos)
	{
		if (tilePositionQuery == default)
		{
			tilePositionQuery = Core.EntityManager.CreateEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {
					new(Il2CppType.Of<TilePosition>(), ComponentType.AccessMode.ReadOnly),
					new(Il2CppType.Of<Translation>(), ComponentType.AccessMode.ReadOnly)
				},
				Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludeSpawnTag
			});
		}

		var closestEntity = Entity.Null;
		var closestDistance = float.MaxValue;
		var entities = tilePositionQuery.ToEntityArray(Allocator.Temp);
		for (var i = 0; i < entities.Length; ++i)
		{
			var entity = entities[i];
			if (!entity.Has<TilePosition>()) continue;
			var entityPos = entity.Read<Translation>().Value;
			var distance = math.distancesq(pos, entityPos);
			if (distance < closestDistance)
			{
				var prefabName = GetPrefabGUID(entity).LookupName();
				if (!prefabName.StartsWith("TM_")) continue;

				closestDistance = distance;
				closestEntity = entity;
			}
		}
		entities.Dispose();

		return closestEntity;
	}
    public static int GetEntityTerritoryIndex(Entity entity)
    {
        if (entity.Has<TilePosition>())
        {
            var pos = entity.Read<TilePosition>().Tile;
            var territoryIndex = Core.CastleTerritory.GetTerritoryIndexFromTileCoord(pos);
            if (territoryIndex != -1)
            {
                return territoryIndex;
            }
        }

        if (entity.Has<TileBounds>())
        {
            var bounds = entity.Read<TileBounds>().Value;
            for (var x = bounds.Min.x; x <= bounds.Max.x; x++)
            {
                for (var y = bounds.Min.y; y <= bounds.Max.y; y++)
                {
                    var territoryIndex = Core.CastleTerritory.GetTerritoryIndexFromTileCoord(new int2(x, y));
                    if (territoryIndex != -1)
                    {
                        return territoryIndex;
                    }
                }
            }
        }

        if (entity.Has<Translation>())
        {
            var pos = entity.Read<Translation>().Value;
            return Core.CastleTerritory.GetTerritoryIndex(pos);
        }

        if (entity.Has<LocalToWorld>())
        {
            var pos = entity.Read<LocalToWorld>().Position;
            return Core.CastleTerritory.GetTerritoryIndex(pos);
        }

        return -1;
    }
    public static IEnumerable<Entity> GetAllEntitiesInTerritory<T>(int territoryIndex)
    {
        var entities = GetEntitiesByComponentType<T>(includeSpawn: true, includeDisabled: true);
        foreach (var entity in entities)
        {
            if (GetEntityTerritoryIndex(entity) == territoryIndex)
            {
                yield return entity;
            }
        }
        entities.Dispose();
    }
    public static Entity FindClosestTilePosition<T>(Vector3 pos, float maxDist=5f)
	{
		if (tilePositionQuery == default)
		{
			tilePositionQuery = Core.EntityManager.CreateEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {
					new(Il2CppType.Of<TilePosition>(), ComponentType.AccessMode.ReadOnly),
					new(Il2CppType.Of<Translation>(), ComponentType.AccessMode.ReadOnly),
					new(Il2CppType.Of<T>(), ComponentType.AccessMode.ReadOnly)
				},
				Options = EntityQueryOptions.IncludeDisabled | EntityQueryOptions.IncludeSpawnTag
			});
		}

		var closestEntity = Entity.Null;
		var closestDistance = maxDist;
		var entities = tilePositionQuery.ToEntityArray(Allocator.Temp);
		for (var i = 0; i < entities.Length; ++i)
		{
			var entity = entities[i];
			if (!entity.Has<TilePosition>()) continue;
			var entityPos = entity.Read<Translation>().Value;
			var distance = math.distancesq(pos, entityPos);
			if (distance < closestDistance)
			{
				var prefabName = GetPrefabGUID(entity).LookupName();
				if (!prefabName.StartsWith("TM_")) continue;

				closestDistance = distance;
				closestEntity = entity;
			}
		}
		entities.Dispose();

		return closestEntity;
	}

	public static void RepairGear(Entity Character, bool repair = true)
	{
		Equipment equipment = Character.Read<Equipment>();
		NativeList<Entity> equippedItems = new(Allocator.Temp);
		equipment.GetAllEquipmentEntities(equippedItems);
		foreach (var equippedItem in equippedItems)
		{
			if (equippedItem.Has<Durability>())
			{
				var durability = equippedItem.Read<Durability>();
				if (repair)
				{
					durability.Value = durability.MaxDurability;
				}
				else
				{
					durability.Value = 0;
				}

				equippedItem.Write(durability);
			}
		}
		equippedItems.Dispose();

		for (int i = 0; i < 36; i++)
		{
			if (InventoryUtilities.TryGetItemAtSlot(Core.EntityManager, Character, i, out InventoryBuffer item))
			{
				var itemEntity = item.ItemEntity._Entity;
				if (itemEntity.Has<Durability>())
				{
					var durability = itemEntity.Read<Durability>();
					if (repair)
					{
						durability.Value = durability.MaxDurability;
					}
					else
					{
						durability.Value = 0;
					}

					itemEntity.Write(durability);
				}
			}
		}
	}

	public static void ReviveCharacter(Entity Character, Entity User, ChatCommandContext ctx = null)
	{
		var health = Character.Read<Health>();
		ctx?.Reply("TryGetbuff");
		if (BuffUtility.TryGetBuff(Core.EntityManager, Character, Prefabs.Buff_General_Vampire_Wounded_Buff, out var buffData))
		{
			ctx?.Reply("Destroy");
			DestroyUtility.Destroy(Core.EntityManager, buffData, DestroyDebugReason.TryRemoveBuff);

			ctx?.Reply("Health");
			health.Value = health.MaxHealth;
			health.MaxRecoveryHealth = health.MaxHealth;
			Character.Write(health);
		}
		if (health.IsDead)
		{
			ctx?.Reply("Respawn");
			var pos = Character.Read<LocalToWorld>().Position;

			Nullable_Unboxed<float3> spawnLoc = new() { value = pos };

			ctx?.Reply("Respawn2");
			var sbs = Core.Server.GetExistingSystemManaged<ServerBootstrapSystem>();
			var bufferSystem = Core.Server.GetExistingSystemManaged<EntityCommandBufferSystem>();
			var buffer = bufferSystem.CreateCommandBuffer();
			ctx?.Reply("Respawn3");
			sbs.RespawnCharacter(buffer, User,
				customSpawnLocation: spawnLoc,
				previousCharacter: Character);
		}
    }

	public static void KickPlayer(Entity userEntity)
	{
		EntityManager entityManager = Core.Server.EntityManager;
		User user = userEntity.Read<User>();

		if (!user.IsConnected || user.PlatformId==0) return;

		Entity entity =  entityManager.CreateEntity(new ComponentType[3]
		{
			ComponentType.ReadOnly<NetworkEventType>(),
			ComponentType.ReadOnly<SendEventToUser>(),
			ComponentType.ReadOnly<KickEvent>()
		});

		entity.Write(new KickEvent()
		{
			PlatformId = user.PlatformId
		});
		entity.Write(new SendEventToUser()
		{
			UserIndex = user.Index
		});
		entity.Write(new NetworkEventType()
		{
			EventId = NetworkEvents.EventId_KickEvent,
			IsAdminEvent = false,
			IsDebugEvent = false
		});
	}

	public static void UnlockWaypoints(Entity userEntity)
	{
		DynamicBuffer<UnlockedWaypointElement> dynamicBuffer = Core.EntityManager.AddBuffer<UnlockedWaypointElement>(userEntity);
		dynamicBuffer.Clear();
		foreach (Entity waypoint in Helper.GetEntitiesByComponentType<ChunkWaypoint>())
			dynamicBuffer.Add(new UnlockedWaypointElement()
			{
				Waypoint = waypoint.Read<NetworkId>()
			});
	}

	public static void RevealMapForPlayer(Entity userEntity)
	{
		var mapZoneElements = Core.EntityManager.GetBuffer<UserMapZoneElement>(userEntity);
		foreach (var mapZone in mapZoneElements)
		{
			var userZoneEntity = mapZone.UserZoneEntity.GetEntityOnServer();
			var revealElements = Core.EntityManager.GetBuffer<UserMapZonePackedRevealElement>(userZoneEntity);
			revealElements.Clear();
			var revealElement = new UserMapZonePackedRevealElement
			{
				PackedPixel = 255
			};
			for (var i = 0; i < 8192; i++)
			{
				revealElements.Add(revealElement);
			}
		}
	}
	// add the component debugunlock
}
