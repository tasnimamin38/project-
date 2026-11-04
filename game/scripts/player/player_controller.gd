extends CharacterBody3D
class_name AnomalyPlayerController

signal health_changed(value: float)
signal ammo_changed(value: int)

@export var thrust_acceleration: float = 24.0
@export var vertical_acceleration: float = 19.0
@export var maximum_speed: float = 18.0
@export var linear_drag: float = 1.8
@export var mouse_sensitivity: float = 0.0022
@export var weapon_damage: float = 34.0
@export var weapon_range: float = 110.0

@onready var head: Node3D = $Head
@onready var camera: Camera3D = $Head/Camera3D

var health: float = 100.0
var magazine: int = 30
var magazine_capacity: int = 30
var reserve_ammo: int = 120
var _shot_cooldown: float = 0.0
var _is_reloading: bool = false

func _ready() -> void:
    add_to_group("players")
    add_to_group("gravity_affected")
    health_changed.emit(health)
    ammo_changed.emit(magazine)

func _unhandled_input(event: InputEvent) -> void:
    if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
        Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
        return

    if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
        if Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
            Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
            return
        _fire_weapon()

    if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
        rotate_y(-event.relative.x * mouse_sensitivity)
        head.rotate_x(-event.relative.y * mouse_sensitivity)
        head.rotation.x = clampf(head.rotation.x, deg_to_rad(-88.0), deg_to_rad(88.0))

func _physics_process(delta: float) -> void:
    if not is_multiplayer_authority():
        return

    _shot_cooldown = maxf(0.0, _shot_cooldown - delta)

    var planar_input := Input.get_vector("move_left", "move_right", "move_forward", "move_back")
    var thrust_direction := (global_transform.basis * Vector3(planar_input.x, 0.0, planar_input.y)).normalized()
    if thrust_direction.length_squared() > 0.0:
        velocity += thrust_direction * thrust_acceleration * delta

    var vertical_input := Input.get_axis("move_down", "move_up")
    if vertical_input != 0.0:
        velocity += Vector3.UP * vertical_input * vertical_acceleration * delta

    if Input.is_action_just_pressed("reload"):
        reload_weapon()

    if planar_input == Vector2.ZERO and is_zero_approx(vertical_input):
        velocity = velocity.move_toward(Vector3.ZERO, linear_drag * delta)

    if velocity.length() > maximum_speed:
        velocity = velocity.normalized() * maximum_speed

    move_and_slide()

func apply_field_acceleration(acceleration: Vector3, delta: float) -> void:
    if is_multiplayer_authority():
        velocity += acceleration * delta

func take_damage(amount: float) -> void:
    if not is_multiplayer_authority():
        return
    health = maxf(0.0, health - maxf(amount, 0.0))
    health_changed.emit(health)
    if health <= 0.0:
        health = 100.0
        global_position = Vector3(0.0, 2.0, 8.0)
        velocity = Vector3.ZERO
        health_changed.emit(health)

func _fire_weapon() -> void:
    if _shot_cooldown > 0.0 or _is_reloading:
        return
    if magazine <= 0:
        reload_weapon()
        return

    magazine -= 1
    ammo_changed.emit(magazine)
    _shot_cooldown = 0.13

    var origin := camera.global_position
    var destination := origin - camera.global_transform.basis.z * weapon_range
    var query := PhysicsRayQueryParameters3D.create(origin, destination)
    query.exclude = [get_rid()]
    query.collision_mask = 3
    var hit := get_world_3d().direct_space_state.intersect_ray(query)
    if not hit.is_empty():
        var target: Object = hit.get("collider")
        if target != null and target.has_method("apply_damage"):
            target.call("apply_damage", weapon_damage)

func reload_weapon() -> void:
    if _is_reloading or magazine >= magazine_capacity or reserve_ammo <= 0:
        return
    _is_reloading = true
    await get_tree().create_timer(1.25).timeout
    var needed := magazine_capacity - magazine
    var loaded := mini(needed, reserve_ammo)
    magazine += loaded
    reserve_ammo -= loaded
    _is_reloading = false
    ammo_changed.emit(magazine)
