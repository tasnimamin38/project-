extends Node

const SAVE_PATH: String = "user://anomaly_profile.cfg"
const CURRENT_SCHEMA: int = 1

var profile: Dictionary = {
    "schema_version": CURRENT_SCHEMA,
    "pilot_name": "Rookie",
    "level": 1,
    "xp": 0,
    "completed_missions": 0
}

func _ready() -> void:
    load_profile()

func load_profile() -> bool:
    if not FileAccess.file_exists(SAVE_PATH):
        return save_profile() == OK

    var config := ConfigFile.new()
    var error := config.load(SAVE_PATH)
    if error != OK:
        push_warning("Profile could not be loaded; keeping a fresh profile.")
        return false

    var stored: Variant = config.get_value("profile", "data", {})
    if not stored is Dictionary:
        push_warning("Profile data has an invalid format.")
        return false

    var schema := int(stored.get("schema_version", 0))
    if schema > CURRENT_SCHEMA:
        push_warning("This save was created by a newer game version; it was not overwritten.")
        return false

    profile.merge(stored, true)
    profile["schema_version"] = CURRENT_SCHEMA
    return true

func save_profile() -> Error:
    var config := ConfigFile.new()
    config.set_value("profile", "data", profile.duplicate(true))
    var error := config.save(SAVE_PATH)
    if error != OK:
        push_error("Could not save ANOMALY profile: %s" % error_string(error))
    return error

func add_xp(amount: int) -> void:
    profile["xp"] = int(profile.get("xp", 0)) + maxi(amount, 0)
    save_profile()

func complete_mission() -> void:
    profile["completed_missions"] = int(profile.get("completed_missions", 0)) + 1
    save_profile()
