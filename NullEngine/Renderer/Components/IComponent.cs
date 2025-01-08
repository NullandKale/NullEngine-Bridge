using NullEngine.Renderer.Mesh;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Renderer.Components
{
    public interface IComponent : ICloneable
    {
        void Update(BaseMesh mesh, float deltaTime);
        void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed);
        void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime);
    }
}
