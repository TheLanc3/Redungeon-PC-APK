using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Redungeon.Core.Abstractions;

public abstract class RedungeonGame : IRedungeonGame {
    private SpriteBatch batch;
    private int lastLoggedWidth;
    private int lastLoggedHeight;
    private int pendingWidth;
    private int pendingHeight;
    private int pendingStableFrames;
    private bool waitingForStableSurface;

    public void Initialize()
    {
        throw new NotImplementedException();
    }

    public void LoadContent()
    {
        throw new NotImplementedException();
    }

    public void UnloadContent()
    {
        throw new NotImplementedException();
    }

    public void Update(GameTime gameTime)
    {
        throw new NotImplementedException();
    }

    public void Draw(GameTime gameTime)
    {
        throw new NotImplementedException();
    }

    public void OnEnteringBackground()
    {
        throw new NotImplementedException();
    }
}
