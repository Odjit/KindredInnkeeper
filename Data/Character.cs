using System;
using System.Collections.Generic;
using Stunlock.Core;

namespace KindredInnkeeper.Data;
internal static class Character
{
	public static void Populate()
	{
		foreach(var (name, prefabGuid) in Core.PrefabCollectionSystem._SpawnableNameToPrefabGuidDictionary)
		{
			if (!name.StartsWith("CHAR")) continue;
			Named[name] = prefabGuid;
			NameFromPrefab[prefabGuid.GuidHash] = name;
		}
	}
	public static Dictionary<string, PrefabGUID> Named = new(StringComparer.OrdinalIgnoreCase);
	public static Dictionary<int, string> NameFromPrefab = new();

}
