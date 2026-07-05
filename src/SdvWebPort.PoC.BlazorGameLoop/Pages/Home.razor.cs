using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace SdvWebPort.PoC.BlazorGameLoop.Pages;

public partial class Home : ComponentBase
{
    private LoopGame? _game;

    // JsRuntime is injected via @inject in Home.razor — no need to redeclare here.

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        if (firstRender)
        {
            // Register this component as a .NET object reference so JS can call
            // back into our TickDotNet method via invokeMethod('TickDotNet').
            // This is the KNI Blazor pattern: the game loop is driven by JS
            // requestAnimationFrame, not by Game.Run() blocking.
            JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public void TickDotNet()
    {
        // First call: initialize the game and call Run() once.
        // Run() does Initialize + LoadContent + one Update, then returns
        // (because StartGameLoop is an empty stub in KNI).
        if (_game == null)
        {
            Console.WriteLine("[Home.TickDotNet] First tick — creating LoopGame + Run()");
            _game = new LoopGame();
            _game.Run();
            Console.WriteLine("[Home.TickDotNet] Run() returned — game initialized");
        }

        // Every call: tick the game loop manually (Update + Draw).
        // This is the external game loop driven by JS requestAnimationFrame.
        _game.Tick();
    }
}
