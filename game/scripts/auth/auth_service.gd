extends Node
class_name AnomalyAuthService

const API_CLIENT_SCRIPT = preload("res://scripts/services/api_client.gd")

var _api: Node
var _access_token: String = ""

func _ready() -> void:
    _api = API_CLIENT_SCRIPT.new()
    _api.base_url = str(ProjectSettings.get_setting("anomaly/api_base_url", ""))
    add_child(_api)

func sign_in(email: String, password: String) -> Dictionary:
    if email.strip_edges().is_empty() or password.is_empty():
        return {"ok": false, "error": "EMAIL_AND_PASSWORD_REQUIRED"}

    var result: Dictionary = await _api.post_json(
        "/v1/auth/sign-in",
        {"email": email.strip_edges(), "password": password}
    )
    if result.get("ok", false):
        var data: Dictionary = result.get("data", {})
        _access_token = str(data.get("access_token", ""))
        if _access_token.is_empty():
            return {"ok": false, "error": "ACCESS_TOKEN_MISSING"}
    return result

func sign_out() -> void:
    _access_token = ""

func has_session() -> bool:
    return not _access_token.is_empty()

func get_access_token_for_request() -> String:
    return _access_token
