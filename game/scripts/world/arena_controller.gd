extends Node3D

const PLAYER_SCENE: PackedScene = preload("res://scenes/player/player.tscn")
const ENEMY_SCENE: PackedScene = preload("res://scenes/enemies/drone_enemy.tscn")
const INPUT_SETUP = preload("res://scripts/input/input_setup.gd")
const HUD_SCRIPT = preload("res://scripts/ui/hud_controller.gd")
const GRAVITY_SCRIPT = preload("res://scripts/physics/anti_gravity_field.gd")

func _ready() -> void:
    INPUT_SETUP.configure()
    _build_environment()
    _build_arena()
    _spawn_player()
    _spawn_enemies()
    _spawn_gravity_field()

    if OS.get_cmdline_user_args().has("--server"):
        var result: int = NetworkService.start_server(7000, 16)
        if result != OK:
            push_error("Could not start ANOMALY dedicated server: %s" % error_string(result))
        else:
            GameState.set_session_mode(GameState.SessionMode.DEDICATED_SERVER)

    var hud := CanvasLayer.new()
    hud.set_script(HUD_SCRIPT)
    add_child(hud)

func _build_environment() -> void:
    var world := WorldEnvironment.new()
    var environment := Environment.new()
    environment.background_mode = Environment.BG_COLOR
    environment.background_color = Color(0.008, 0.014, 0.028)
    environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
    environment.ambient_light_color = Color(0.22, 0.34, 0.48)
    environment.ambient_light_energy = 0.55
    world.environment = environment
    add_child(world)

    var key_light := DirectionalLight3D.new()
    key_light.rotation_degrees = Vector3(-48.0, -28.0, 0.0)
    key_light.light_energy = 1.25
    add_child(key_light)

func _build_arena() -> void:
    var floor_body := StaticBody3D.new()
    floor_body.name = "ArenaFloor"
    floor_body.position = Vector3(0.0, -5.0, 0.0)
    add_child(floor_body)

    var floor_shape := CollisionShape3D.new()
    var box_shape := BoxShape3D.new()
    box_shape.size = Vector3(90.0, 1.0, 90.0)
    floor_shape.shape = box_shape
    floor_body.add_child(floor_shape)

    var floor_mesh := MeshInstance3D.new()
    var box_mesh := BoxMesh.new()
    box_mesh.size = box_shape.size
    floor_mesh.mesh = box_mesh
    var floor_material := StandardMaterial3D.new()
    floor_material.albedo_color = Color(0.025, 0.045, 0.075)
    floor_material.metallic = 0.72
    floor_material.roughness = 0.42
    floor_mesh.material_override = floor_material
    floor_body.add_child(floor_mesh)

    for index in range(8):
        var angle := TAU * float(index) / 8.0
        var pillar := MeshInstance3D.new()
        var pillar_mesh := CylinderMesh.new()
        pillar_mesh.top_radius = 0.45
        pillar_mesh.bottom_radius = 0.7
        pillar_mesh.height = 11.0
        pillar.mesh = pillar_mesh
        pillar.position = Vector3(cos(angle) * 31.0, 0.5, sin(angle) * 31.0)
        var pillar_material := StandardMaterial3D.new()
        pillar_material.albedo_color = Color(0.02, 0.22, 0.31)
        pillar_material.emission_enabled = true
        pillar_material.emission = Color(0.0, 0.32, 0.48)
        pillar_material.emission_energy_multiplier = 1.2
        pillar.material_override = pillar_material
        add_child(pillar)

func _spawn_player() -> void:
    var player := PLAYER_SCENE.instantiate()
    player.name = "Player_%s" % multiplayer.get_unique_id()
    add_child(player)
    player.global_position = Vector3(0.0, 1.0, 8.0)

func _spawn_enemies() -> void:
    var spawn_points: Array[Vector3] = [
        Vector3(9.0, 2.0, -8.0),
        Vector3(-12.0, 5.0, -4.0),
        Vector3(5.0, 8.0, -18.0),
        Vector3(-18.0, -1.0, 12.0)
    ]
    for spawn_point in spawn_points:
        var enemy := ENEMY_SCENE.instantiate()
        add_child(enemy)
        enemy.global_position = spawn_point

func _spawn_gravity_field() -> void:
    var field := Node3D.new()
    field.name = "NexusGravityField"
    field.set_script(GRAVITY_SCRIPT)
    add_child(field)
    field.global_position = Vector3(0.0, 2.0, -2.0)
