extends CanvasLayer

var _status_label: Label
var _hint_label: Label

func _ready() -> void:
    _status_label = Label.new()
    _status_label.position = Vector2(24.0, 22.0)
    _status_label.add_theme_font_size_override("font_size", 20)
    _status_label.add_theme_color_override("font_color", Color(0.55, 0.94, 1.0))
    add_child(_status_label)

    _hint_label = Label.new()
    _hint_label.text = "WASD THRUST  ·  SPACE/CTRL VERTICAL  ·  CLICK FIRE  ·  R RELOAD  ·  ESC MOUSE"
    _hint_label.anchor_left = 0.0
    _hint_label.anchor_top = 1.0
    _hint_label.anchor_right = 1.0
    _hint_label.anchor_bottom = 1.0
    _hint_label.offset_left = 24.0
    _hint_label.offset_top = -42.0
    _hint_label.offset_right = -24.0
    _hint_label.offset_bottom = -16.0
    _hint_label.add_theme_font_size_override("font_size", 13)
    _hint_label.add_theme_color_override("font_color", Color(0.55, 0.7, 0.82))
    add_child(_hint_label)

func _process(_delta: float) -> void:
    var player := get_tree().get_first_node_in_group("players")
    var drones := get_tree().get_nodes_in_group("enemies").size()
    if player != null:
        _status_label.text = "ANOMALY // NEXUS-0\nHULL %03d   AMMO %02d   HOSTILES %02d\nSESSION %s" % [
            roundi(player.health),
            player.magazine,
            drones,
            str(GameState.session_mode)
        ]
    else:
        _status_label.text = "ANOMALY // INITIALIZING"
