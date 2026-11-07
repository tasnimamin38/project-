extends SceneTree

func _initialize() -> void:
    call_deferred("_run_smoke_test")


func _run_smoke_test() -> void:
    if not _test_api_base_url_validation():
        quit(1)
        return

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

    print("ANOMALY smoke test passed: URL validation, main scene and player initialized.")
    quit(0)


func _test_api_base_url_validation() -> bool:
    var valid_urls := [
        "https://api.example.com",
        "https://api.example.com/v1/",
        "http://localhost:8787",
        "http://127.0.0.1:8787",
        "http://[::1]:8787"
    ]
    for url in valid_urls:
        if not AnomalyApiClient.is_base_url_allowed(url):
            push_error("Expected API base URL to be allowed: " + url)
            return false

    var invalid_urls := [
        "",
        "api.example.com",
        "ftp://localhost",
        "http://api.example.com",
        "http://localhost.evil.example",
        "http://127.0.0.10:8787",
        "http://user@localhost:8787",
        "http://localhost:",
        "http://localhost:99999",
        "http://[::1].evil.example"
    ]
    for url in invalid_urls:
        if AnomalyApiClient.is_base_url_allowed(url):
            push_error("Expected API base URL to be rejected: " + url)
            return false

    print("ANOMALY API URL security tests passed.")
    return true
