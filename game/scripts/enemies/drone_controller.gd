extends CharacterBody3D
class_name AnomalyDroneController

@export var maximum_health: float = 110.0
@export var move_speed: float = 4.2
@export var contact_range: float = 1.65
@export var contact_damage: float = 9.0
@export var attack_interval: float = 1.1

var health: float = 110.0
var _attack_timer: float = 0.0
var _target: Node3D

func _ready() -> void:
    health = maximum_health
    add_to_group("gravity_affected")
    add_to_group("enemies")

func _physics_process(delta: float) -> void:
    _attack_timer = maxf(0.0, _attack_timer - delta)
    if not is_instance_valid(_target):
        _target = get_tree().get_first_node_in_group("players") as Node3D
    if not is_instance_valid(_target):
        velocity = Vector3.ZERO
        return

    var direction := _target.global_position - global_position
    var distance := direction.length()
    if distance > contact_range:
        velocity = direction.normalized() * move_speed
    else:
        velocity = Vector3.ZERO
        if _attack_timer <= 0.0 and _target.has_method("take_damage"):
            _target.call("take_damage", contact_damage)
            _attack_timer = attack_interval

    move_and_slide()

func apply_field_acceleration(acceleration: Vector3, delta: float) -> void:
    velocity += acceleration * delta

func apply_damage(amount: float) -> void:
    health = maxf(0.0, health - maxf(amount, 0.0))
    if health <= 0.0:
        SaveService.add_xp(25)
        queue_free()
