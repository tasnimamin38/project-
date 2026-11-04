extends Node

signal server_started(port: int)
signal connected_to_server
signal connection_failed
signal peer_joined_session(peer_id: int)
signal peer_left_session(peer_id: int)
signal session_ended

var _peer: ENetMultiplayerPeer

func start_server(port: int = 7000, maximum_clients: int = 16) -> int:
    close_session()
    _peer = ENetMultiplayerPeer.new()
    var error := _peer.create_server(port, maximum_clients)
    if error != OK:
        _peer = null
        return error

    multiplayer.multiplayer_peer = _peer
    _connect_signals()
    GameState.set_session_mode(GameState.SessionMode.HOST)
    server_started.emit(port)
    return OK

func join_server(address: String, port: int = 7000) -> int:
    close_session()
    if address.strip_edges().is_empty():
        return ERR_INVALID_PARAMETER

    _peer = ENetMultiplayerPeer.new()
    var error := _peer.create_client(address.strip_edges(), port)
    if error != OK:
        _peer = null
        return error

    multiplayer.multiplayer_peer = _peer
    _connect_signals()
    GameState.set_session_mode(GameState.SessionMode.CLIENT)
    return OK

func close_session() -> void:
    if _peer != null:
        _peer.close()
        _peer = null
    multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
    GameState.set_session_mode(GameState.SessionMode.SOLO)
    session_ended.emit()

func is_host() -> bool:
    return multiplayer.is_server() and _peer != null

func local_peer_id() -> int:
    return multiplayer.get_unique_id()

func _connect_signals() -> void:
    if not multiplayer.peer_connected.is_connected(_on_peer_connected):
        multiplayer.peer_connected.connect(_on_peer_connected)
    if not multiplayer.peer_disconnected.is_connected(_on_peer_disconnected):
        multiplayer.peer_disconnected.connect(_on_peer_disconnected)
    if not multiplayer.connected_to_server.is_connected(_on_connected):
        multiplayer.connected_to_server.connect(_on_connected)
    if not multiplayer.connection_failed.is_connected(_on_connection_failed):
        multiplayer.connection_failed.connect(_on_connection_failed)

func _on_peer_connected(peer_id: int) -> void:
    peer_joined_session.emit(peer_id)

func _on_peer_disconnected(peer_id: int) -> void:
    peer_left_session.emit(peer_id)

func _on_connected() -> void:
    connected_to_server.emit()

func _on_connection_failed() -> void:
    connection_failed.emit()
    close_session()
