# init-carpetas.ps1
# Crea solo la estructura de carpetas (sin archivos)

$root = "brazo-rv-joystick"

$dirs = @(
  "$root/docs",
  "$root/design/joystick",

  "$root/hardware/joystick/electronics",
  "$root/hardware/joystick/mechanics",
  "$root/hardware/joystick/firmware/src/drivers",
  "$root/hardware/joystick/firmware/src/hid",
  "$root/hardware/joystick/firmware/src/proto",
  "$root/hardware/joystick/tools",
  "$root/hardware/joystick/tests",

  "$root/Tools",

  "$root/Packages.local/com.tuorg.device.joystick.core/Runtime/Proto",
  "$root/Packages.local/com.tuorg.device.joystick.core/Runtime/Calib",
  "$root/Packages.local/com.tuorg.device.joystick.core/Runtime/Diagnostics",
  "$root/Packages.local/com.tuorg.device.joystick.core/Tests",

  "$root/Packages.local/com.tuorg.device.joystick.unity/Runtime/InputSystem",
  "$root/Packages.local/com.tuorg.device.joystick.unity/Runtime/SerialBackend",
  "$root/Packages.local/com.tuorg.device.joystick.unity/Runtime/Editor",
  "$root/Packages.local/com.tuorg.device.joystick.unity/Samples~",

  "$root/ProjectSettings",
  "$root/Packages",

  "$root/UnityProject/Assets/_Project/Art",
  "$root/UnityProject/Assets/_Project/Audio",
  "$root/UnityProject/Assets/_Project/Materials",
  "$root/UnityProject/Assets/_Project/Prefabs/Arm",
  "$root/UnityProject/Assets/_Project/Prefabs/UI",
  "$root/UnityProject/Assets/_Project/Scenes/90_Testbeds",
  "$root/UnityProject/Assets/_Project/Scripts/Core/Kinematics",
  "$root/UnityProject/Assets/_Project/Scripts/Core/Control",
  "$root/UnityProject/Assets/_Project/Scripts/Core/Utils",
  "$root/UnityProject/Assets/_Project/Scripts/Input/Joystick",
  "$root/UnityProject/Assets/_Project/Scripts/Input/XR",
  "$root/UnityProject/Assets/_Project/Scripts/VR/Rig",
  "$root/UnityProject/Assets/_Project/Scripts/VR/Interactables",
  "$root/UnityProject/Assets/_Project/Scripts/UI",
  "$root/UnityProject/Assets/_Project/Scripts/Physics",
  "$root/UnityProject/Assets/_Project/Scripts/Editor",
  "$root/UnityProject/Assets/_Project/Settings",
  "$root/UnityProject/Assets/_Project/Addressables",
  "$root/UnityProject/Assets/_Project/Tests",
  "$root/UnityProject/Assets/ThirdParty",

  "$root/.github/workflows",
  "$root/.vscode"
)

foreach ($d in $dirs) {
  New-Item -ItemType Directory -Path $d -Force | Out-Null
}

Write-Host "Estructura creada en: $(Resolve-Path $root)"
