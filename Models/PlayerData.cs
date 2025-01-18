using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace KindredInnkeeper.Models;

public struct PlayerData(FixedString64Bytes characterName = default, ulong steamID = 0, bool isOnline = false, Entity userEntity = default, Entity charEntity = default)
{
	public FixedString64Bytes CharacterName { get; set; } = characterName;
	public ulong SteamID { get; set; } = steamID;
	public bool IsOnline { get; set; } = isOnline;
	public Entity UserEntity { get; set; } = userEntity;
	public Entity CharEntity { get; set; } = charEntity;

}
public class Player
{
	public string Name { get; set; }
	public ulong SteamID { get; set; }
	public bool IsOnline { get; set; }
	public bool IsAdmin { get; set; }
	public Entity User { get; set; }
	public Entity Character { get; set; }
	public Player(Entity userEntity = default)
	{
		User = userEntity;
		var user = User.Read<User>();
		Character = user.LocalCharacter._Entity;
		Name = user.CharacterName.ToString();
		IsOnline = user.IsConnected;
		IsAdmin = user.IsAdmin;
		SteamID = user.PlatformId;
	}
}
