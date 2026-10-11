using GameTime = Microsoft.Xna.Framework.GameTime;

namespace Redungeon.Core.Abstractions;

public interface IRedungeonGame {
    void Initialize();
    void LoadContent();
    void UnloadContent();
    void Update(GameTime gameTime);
    void Draw(GameTime gameTime);
    void OnEnteringBackground();
}