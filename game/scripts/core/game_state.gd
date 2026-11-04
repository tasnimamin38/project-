extends Node

signal session_mode_changed(mode: int)
signal world_changed(world_id: String)

enum SessionMode { SOLO, HOST, CLIENT, DEDICATED_SERVER }

var session_mode: SessionMode = SessionMode.SOLO
var selected_world: String = "Nexus-0"
var match_id: String = ""
var is_paused: bool = false

func set_session_mode(value: SessionMode) -> void:
    session_mode = value
    session_mode_changed.emit(session_mode)

func select_world(world_id: String) -> void:
    selected_world = world_id
    world_changed.emit(selected_world)
