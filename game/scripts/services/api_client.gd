extends Node
class_name AnomalyApiClient

@export var base_url: String = ""
@export var request_timeout_seconds: float = 15.0

var _http: HTTPRequest

func _ready() -> void:
    _http = HTTPRequest.new()
    _http.timeout = request_timeout_seconds
    add_child(_http)


static func is_base_url_allowed(candidate: String) -> bool:
    var value := candidate.strip_edges()
    var separator := value.find("://")
    if separator <= 0:
        return false

    var scheme := value.substr(0, separator).to_lower()
    if scheme != "https" and scheme != "http":
        return false

    var remainder := value.substr(separator + 3)
    var authority := remainder.split("/", true, 1)[0]
    authority = authority.split("?", true, 1)[0]
    authority = authority.split("#", true, 1)[0]
    if authority.is_empty() or authority.contains("@") or authority.contains(" "):
        return false

    var hostname := ""
    var port_text := ""
    if authority.begins_with("["):
        var closing_bracket := authority.find("]")
        if closing_bracket < 0:
            return false
        hostname = authority.substr(0, closing_bracket + 1).to_lower()
        var suffix := authority.substr(closing_bracket + 1)
        if not suffix.is_empty():
            if not suffix.begins_with(":"):
                return false
            port_text = suffix.substr(1)
    else:
        var colon := authority.find(":")
        if colon >= 0:
            if authority.find(":", colon + 1) >= 0:
                return false
            hostname = authority.substr(0, colon).to_lower()
            port_text = authority.substr(colon + 1)
        else:
            hostname = authority.to_lower()

    if hostname.is_empty():
        return false
    if not port_text.is_empty():
        if not port_text.is_valid_int():
            return false
        var port := int(port_text)
        if port < 1 or port > 65535:
            return false
    elif authority.ends_with(":"):
        return false

    if scheme == "https":
        return true

    return hostname == "localhost" or hostname == "127.0.0.1" or hostname == "[::1]"


func post_json(path: String, payload: Dictionary, bearer_token: String = "") -> Dictionary:
    if base_url.strip_edges().is_empty():
        return {"ok": false, "error": "API_BASE_URL_NOT_CONFIGURED"}

    var normalized_base := base_url.trim_suffix("/")
    if not is_base_url_allowed(normalized_base):
        return {"ok": false, "error": "HTTPS_REQUIRED_OR_UNTRUSTED_LOCAL_HOST"}

    var normalized_path := path.trim_prefix("/")
    var url := normalized_base + "/" + normalized_path

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
    var request_result: int = int(response[0])
    var response_code: int = int(response[1])
    var body: PackedByteArray = response[3]
    var parsed: Variant = JSON.parse_string(body.get_string_from_utf8())

    return {
        "ok": request_result == HTTPRequest.RESULT_SUCCESS and response_code >= 200 and response_code < 300,
        "network_result": request_result,
        "status": response_code,
        "data": parsed
    }
