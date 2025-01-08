# NullEngine

This is my 3D engine based on the sample code from the Bridge SDK, specifically [this project](https://github.com/Looking-Glass/bridge-sdk-samples/tree/csharp_samples/BridgeSDKSample).

---

## How to Get Started

This is a basic renderer built on OpenGL using OpenTK. I’ve added support for the Bridge SDK and implemented a few asset loaders and a basic Entity-Component System (ECS), similar to a very basic Unity.

If you want to make changes, you can either directly edit the `MainWindow.cs` class and other files, OR you can create a new project with a project reference to the `NullEngine` project and extend my `MainWindow` class.

---

### Step 0: Install Prerequisites

You will need the .NET toolchain and a text editor. I recommend either [Visual Studio](https://visualstudio.microsoft.com/vs/community/) or [VS Code](https://code.visualstudio.com/). 

- If you’re using **Visual Studio**, install the **C# Desktop Development** workload during setup.
- If you’re using **VS Code**, you’ll need to install the [.NET SDK](https://dotnet.microsoft.com/en-us/download) separately.
- Install the **C# extension** in VS Code for a better development experience.

After cloning the repository, make sure to restore the NuGet packages to avoid build or runtime errors.

---

### Step 1: Running the Sample Project

The `NullEngine` project renders a cube in the display. It may be slightly out of focus, but you can adjust this using the **Arrow Up** and **Arrow Down** keys.

Here’s how to run the project:

```
# Clone the repository
git clone https://github.com/NullandKale/NullEngine-Bridge.git

# Navigate to the project directory
cd NullEngine-Bridge

# Restore NuGet packages
dotnet restore

# Run the project
cd NullEngine
dotnet run
```

---

### Step 2: Extending the Engine

If you want to extend the engine without modifying the core `NullEngine` project, you can create a new project and reference `NullEngine` as a dependency. Here’s an example:

1. **Create a new console project**:
    ```
    dotnet new console -n MyCustomEngine
    cd MyCustomEngine
    ```

2. **Add a project reference to `NullEngine`**:
   ```
   dotnet add reference ../NullEngine/NullEngine.csproj
   ```

3. **Create a custom `MainWindow` class that extends `NullEngine.MainWindow`**:
   ```
   using NullEngine;

   public class CustomMainWindow : MainWindow
   {
       protected override (string SceneFilePath, string ActiveSceneName)[] GetScenes()
       {
           return new[]
           {
               ("Assets/Scenes/CustomScene1.json", "CustomScene1"),
               ("Assets/Scenes/CustomScene2.json", "CustomScene2"),
           };
       }

       protected override int GetSceneIndex()
       {
           return 1; // Default to the second scene
       }
   }

   class Program
   {
       static void Main(string[] args)
       {
           using var window = new CustomMainWindow();
           window.Run();
       }
   }
   ```

4. **Run your custom project**:
   ```
   dotnet run
   ```

---

### How Scenes Work

The engine uses a JSON-based scene system to define objects, their properties, and their behaviors. Each scene is defined in a JSON file and contains the following:

- **Camera Settings**: Controls the camera’s size, focus, and offset.
- **Transform**: Defines the position, rotation, and scale of objects in the scene.
- **Meshes**: Represents 3D objects in the scene, such as cubes or other models.
- **Components**: Attaches behaviors to meshes, such as movement or scene switching.

Here’s an example scene definition:

```
{
  "TestScene0": {
    "CameraSize": 2.0,
    "Focus": 1.0,
    "Offset": 1.0,
    "Transform": {
      "Position": [ 0.0, 0.0, 0.0 ],
      "Rotation": [ 0.0, 0.0, 0.0 ],
      "Scale": [ 1.0, 1.0, 1.0 ]
    },
    "Meshes": [
      {
        "MeshName": "cube0",
        "MeshParameters": {
          "Type": "Cube",
          "Size": 1.0
        },
        "Transform": {
          "Position": [ 0.0, 0.0, 0.0 ],
          "Rotation": [ 0.0, 45.0, 45.0 ],
          "Scale": [ 1.0, 1.0, 1.0 ]
        },
        "Components": [
          {
            "Type": "SceneChangeComponent",
            "Properties": {
              "Scenes": [ "TestScene1", "", "", "", "", "", "", "", "", "" ]
            }
          },
          {
            "Type": "SceneMoveComponent",
            "Properties": {
              "MovementSpeed": 5.0,
              "RotationSensitivity": 0.1
            }
          }
        ]
      }
    ]
  },
  "TestScene1": {
    "CameraSize": 2.0,
    "Focus": 1.0,
    "Offset": 1.0,
    "Transform": {
      "Position": [ 0.0, 0.0, 0.0 ],
      "Rotation": [ 0.0, 0.0, 0.0 ],
      "Scale": [ 0.5, 0.5, 0.5 ]
    },
    "Meshes": [
      {
        "MeshName": "cube1",
        "MeshParameters": {
          "Type": "Cube",
          "Size": 1.0
        },
        "Transform": {
          "Position": [ 0.0, 0.0, 0.0 ],
          "Rotation": [ 0.0, 90.0, 0.0 ],
          "Scale": [ 1.0, 1.0, 1.0 ]
        },
        "Components": [
          {
            "Type": "SceneChangeComponent",
            "Properties": {
              "Scenes": [ "TestScene0", "", "", "", "", "", "", "", "", "" ]
            }
          },
          {
            "Type": "SceneMoveComponent",
            "Properties": {
              "MovementSpeed": 5.0,
              "RotationSensitivity": 0.1
            }
          }
        ]
      }
    ]
  }
}
```

#### Key Features of the Scene System:
- **Camera Settings**: The `CameraSize`, `Focus`, and `Offset` properties control how the camera views the scene.
- **Transform**: Each mesh has a `Transform` property that defines its position, rotation, and scale in 3D space.
- **Meshes**: Meshes represent 3D objects. In this example, a cube is defined with a size of 1.0.
- **Components**: Components add behaviors to meshes. For example:
  - `SceneChangeComponent`: Allows switching between scenes (e.g., from `TestScene0` to `TestScene1`).
  - `SceneMoveComponent`: Adds movement and rotation controls to the mesh.

This system is similar to Unity’s GameObject-Component model, where each object (`Mesh`) can have multiple components that define its behavior.

---

### Contributing

If you find any issues or have suggestions for improvements, feel free to open an issue or submit a pull request. Contributions are welcome!