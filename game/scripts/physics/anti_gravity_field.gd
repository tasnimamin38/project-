extends Node3D
class_name AnomalyAntiGravityField

enum FieldMode { REPEL, ATTRACT }

@export var influence_radius: float = 25.0
@export var field_strength: float = 5.5
@export var mode: FieldMode = FieldMode.REPEL
@export var core_dead_zone: float = 1.5

func _physics_process(delta: float) -> void:
    for body in get_tree().get_nodes_in_group("gravity_affected"):
        if not is_instance_valid(body) or not body is Node3D:
            continue

        var offset: Vector3 = body.global_position - global_position
        var distance := offset.length()
        if distance < core_dead_zone or distance > influence_radius:
            continue

        var direction := offset.normalized()
        if mode == FieldMode.ATTRACT:
            direction = -direction

        var falloff := 1.0 - (distance / influence_radius)
        var acceleration := direction * field_strength * falloff
        if body.has_method("apply_field_acceleration"):
            body.call("apply_field_acceleration", acceleration, delta)
