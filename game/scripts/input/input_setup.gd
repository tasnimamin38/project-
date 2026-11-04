extends RefCounted
class_name AnomalyInputSetup

static func configure() -> void:
    _bind_key("move_forward", KEY_W)
    _bind_key("move_left", KEY_A)
    _bind_key("move_back", KEY_S)
    _bind_key("move_right", KEY_D)
    _bind_key("move_up", KEY_SPACE)
    _bind_key("move_down", KEY_CTRL)
    _bind_key("reload", KEY_R)
    _bind_key("pause", KEY_ESCAPE)

static func _bind_key(action_name: StringName, keycode: int) -> void:
    if not InputMap.has_action(action_name):
        InputMap.add_action(action_name)
    for existing in InputMap.action_get_events(action_name):
        if existing is InputEventKey and existing.physical_keycode == keycode:
            return
    var event := InputEventKey.new()
    event.physical_keycode = keycode
    InputMap.action_add_event(action_name, event)
