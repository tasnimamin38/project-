extends Node
class_name AnomalyApiClient

@export var base_url: String = ""
@export var request_timeout_seconds: float = 15.0

var _http: HTTPRequest

func _ready() -> void:
    _http = HTTPRequest.new()
    _http.timeout = request_timeout_seconds
    add_child(_http)

func post_json(path: String, payload: Dictionary, bearer_token: String = "") -> Dictionary:
    if base_url.strip_edges().is_empty():
        return {"ok": false, "error": "API_BASE_URL_NOT_CONFIGURED"}

    var normalized_base := base_url.trim_suffix("/")
    var normalized_path := path.trim_prefix("/")
    var url := normalized_base + "/" + normalized_path
    var is_local := url.begins_with("http://127.0.0.1") or url.begins_with("http://localhost")
    if not url.begins_with("https://") and not is_local:
        return {"ok": false, "error": "HTTPS_REQUIRED"}

    var headers := PackedStringArray(["Content-Type: application/json"])
    if not bearer_token.is_empty():
        headers.append("Authorization: Bearer " + bearer_token)

    var start_error := _http.request(
        url,
        headers,
        HTTPClient.METHOD_POST,
        JSON.stringify(payload)
    )
    if start_error != OK:
        return {"ok": false, "error": "REQUEST_START_FAILED", "code": start_error}

    var response: Array = await _http.request_completed
    var response_code: int = int(response[1])
    var body: PackedByteArray = response[3]
    var parsed: Variant = JSON.parse_string(body.get_string_from_utf8())

    return {
        "ok": response_code >= 200 and response_code < 300,
        "status": response_code,
        "data": parsed
    }
