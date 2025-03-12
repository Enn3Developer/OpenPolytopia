namespace OpenPolytopia.Server;

public class UserNotRegisteredException() : Exception("User not registered");

public class LobbyNotFoundException() : Exception("Lobby not found");

public class LobbyAlreadyStartedException() : Exception("Lobby has already started a game");

public class AlreadyJoinedLobbyException() : Exception("You already joined this lobby");

public class LobbyFullException() : Exception("This lobby is full");

public class NotInLobbyException() : Exception("You must join a lobby first before leaving it");

public class ReducerNoPermissionException() : Exception("You don't have permission to run this reducer");
