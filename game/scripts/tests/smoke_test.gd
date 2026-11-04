extends SceneTree

func _initialize() -> void:
    call_deferred("_run_smoke_test")

func _run_smoke_test() -> void:
    var main_scene := load("res://scenes/main/main.tscn") as PackedScene
    if main_scene == null:
        push_error("Main scene could not be loaded.")
        quit(1)
        return

    var game_root := main_scene.instantiate()
    get_root().add_child(game_root)
    await process_frame
    await process_frame

    var player_nodes := get_nodes_in_group("players")
    if player_nodes.is_empty():
        push_error("Smoke test failed: player was not spawned.")
        quit(1)
        return

    print("ANOMALY smoke test passed: main scene and player initialized.")
    quit(0)
