@tool
extends EditorPlugin
## Lets the editor open and save the json data files as resources
##
## Select one of the files (see JsonData.cs) in the FileSystem dock to edit it in the inspector, saving writes the
## json back.
##
## This is GDScript on purpose: a plugin with a C# entry script can't load until the assembly is built and the
## editor disables it when that happens, so the C# loader and saver are created here as soon as they can be.
## They must be registered in front of the built-in json ones, so they can't be global classes (the engine
## registers those last); the editor drops every script-backed loader and saver whenever the global classes change,
## so they're registered again after every filesystem update.

const LOADER_SCRIPT := "res://addons/polytopia_data/JsonDataLoader.cs"
const SAVER_SCRIPT := "res://addons/polytopia_data/JsonDataSaver.cs"
const PROBE_SCRIPT := "res://src/Data/TroopsResource.cs"

var _loader: ResourceFormatLoader
var _saver: ResourceFormatSaver


func _enter_tree() -> void:
	var file_system := EditorInterface.get_resource_filesystem()
	file_system.filesystem_changed.connect(_ensure_registered)
	# the loaders are dropped right after this signal, so wait for the frame to end
	file_system.script_classes_updated.connect(_ensure_registered, CONNECT_DEFERRED)
	_ensure_registered()


func _exit_tree() -> void:
	var file_system := EditorInterface.get_resource_filesystem()
	file_system.filesystem_changed.disconnect(_ensure_registered)
	file_system.script_classes_updated.disconnect(_ensure_registered)
	if _loader != null and _is_loader_registered():
		ResourceLoader.remove_resource_format_loader(_loader)
	if _saver != null and _is_saver_registered():
		ResourceSaver.remove_resource_format_saver(_saver)
	_loader = null
	_saver = null


func _ensure_registered() -> void:
	if not _is_loader_registered():
		var loader: ResourceFormatLoader = _instantiate(LOADER_SCRIPT)
		if loader != null:
			_loader = loader
			ResourceLoader.add_resource_format_loader(_loader, true)
	if not _is_saver_registered():
		var saver: ResourceFormatSaver = _instantiate(SAVER_SCRIPT)
		if saver != null:
			_saver = saver
			ResourceSaver.add_resource_format_saver(_saver, true)


## Creates an object from a C# script, or null while the assembly isn't built
func _instantiate(script_path: String) -> Object:
	var script: Script = load(script_path)
	if script == null or not script.can_instantiate():
		return null
	return script.new()


# there's no way to ask the engine whether a loader/saver is registered, but only ours answer "json" for these
func _is_loader_registered() -> bool:
	return "json" in ResourceLoader.get_recognized_extensions_for_type("TroopsResource")


func _is_saver_registered() -> bool:
	var probe: Resource = _instantiate(PROBE_SCRIPT)
	# the saver can't exist without the assembly either
	return probe == null or "json" in ResourceSaver.get_recognized_extensions(probe)
