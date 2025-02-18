﻿#region license

// Copyright (c) 2021, andreakarasho
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
// 1. Redistributions of source code must retain the above copyright
//    notice, this list of conditions and the following disclaimer.
// 2. Redistributions in binary form must reproduce the above copyright
//    notice, this list of conditions and the following disclaimer in the
//    documentation and/or other materials provided with the distribution.
// 3. All advertising materials mentioning features or use of this software
//    must display the following acknowledgement:
//    This product includes software developed by andreakarasho - https://github.com/andreakarasho
// 4. Neither the name of the copyright holder nor the
//    names of its contributors may be used to endorse or promote products
//    derived from this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ''AS IS'' AND ANY
// EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY
// DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
// (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
// LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
// ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
// SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
// MobileUO: import
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.IO.Resources;
using ClassicUO.Network;
using ClassicUO.Renderer;
using ClassicUO.Resources;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDL2;
using static SDL2.SDL;

namespace ClassicUO
{
    internal unsafe class GameController : Microsoft.Xna.Framework.Game
    {
        private SDL_EventFilter _filter;

        private readonly Texture2D[] _hueSamplers = new Texture2D[3];
        private bool _ignoreNextTextInput;
        private readonly float[] _intervalFixedUpdate = new float[2];
        private double _statisticsTimer;
        private double _totalElapsed, _currentFpsTime;
        private uint _totalFrames;
        private UltimaBatcher2D _uoSpriteBatch;
        private bool _suppressedDraw;

        // MobileUO: Batcher and TouchScreenKeyboard
        public UltimaBatcher2D Batcher => _uoSpriteBatch;
        public static UnityEngine.TouchScreenKeyboard TouchScreenKeyboard;

        public GameController()
        {
            GraphicManager = new GraphicsDeviceManager(this);
            // MobileUO: commented out
            //GraphicManager.PreparingDeviceSettings += (sender, e) => { e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.DiscardContents; };

            GraphicManager.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            SetVSync(false);

            Window.ClientSizeChanged += WindowOnClientSizeChanged;
            Window.AllowUserResizing = true;
            Window.Title = $"ClassicUO - {CUOEnviroment.Version}";
            IsMouseVisible = Settings.GlobalSettings.RunMouseInASeparateThread;

            IsFixedTimeStep = false; // Settings.GlobalSettings.FixedTimeStep;
            TargetElapsedTime = TimeSpan.FromMilliseconds(1000.0 / 250.0);
            InactiveSleepTime = TimeSpan.Zero;
        }

        public Scene Scene { get; private set; }
        public GameCursor GameCursor { get; private set; }
        public AudioManager Audio { get; private set; }


        public GraphicsDeviceManager GraphicManager { get; }
        public readonly uint[] FrameDelay = new uint[2];

        protected override void Initialize()
        {
            // MobileUO: commented out
            //if (GraphicManager.GraphicsDevice.Adapter.IsProfileSupported(GraphicsProfile.HiDef))
            //{
            //    GraphicManager.GraphicsProfile = GraphicsProfile.HiDef;
            //}

            GraphicManager.ApplyChanges();

            SetRefreshRate(Settings.GlobalSettings.FPS);
            _uoSpriteBatch = new UltimaBatcher2D(GraphicsDevice);

            _filter = HandleSdlEvent;
            SDL_SetEventFilter(_filter, IntPtr.Zero);

            base.Initialize();
        }

        protected override void LoadContent()
        {
            base.LoadContent();

            const int TEXTURE_WIDTH = 32;
            const int TEXTURE_HEIGHT = 2048;

            const int LIGHTS_TEXTURE_WIDTH = 32;
            const int LIGHTS_TEXTURE_HEIGHT = 63;

            _hueSamplers[0] = new Texture2D(GraphicsDevice, TEXTURE_WIDTH, TEXTURE_HEIGHT);
            _hueSamplers[1] = new Texture2D(GraphicsDevice, TEXTURE_WIDTH, TEXTURE_HEIGHT);
            _hueSamplers[2] = new Texture2D(GraphicsDevice, LIGHTS_TEXTURE_WIDTH, LIGHTS_TEXTURE_HEIGHT);


            uint[] buffer = System.Buffers.ArrayPool<uint>.Shared.Rent(Math.Max(LIGHTS_TEXTURE_WIDTH * LIGHTS_TEXTURE_HEIGHT, TEXTURE_WIDTH * TEXTURE_HEIGHT * 2));

            fixed (uint* ptr = buffer)
            {
                HuesLoader.Instance.CreateShaderColors(buffer);
                // MobileUO: true parameters for invertY
                _hueSamplers[0].SetDataPointerEXT(0, null, (IntPtr) ptr, TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(uint), true);
                _hueSamplers[1].SetDataPointerEXT(0, null, (IntPtr) ptr + TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(uint), TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(uint), true);

                LightColors.CreateLightTextures(buffer, LIGHTS_TEXTURE_HEIGHT);
                _hueSamplers[2].SetDataPointerEXT(0, null, (IntPtr)ptr, LIGHTS_TEXTURE_WIDTH * LIGHTS_TEXTURE_HEIGHT * sizeof(uint), true);
            }      
        
            System.Buffers.ArrayPool<uint>.Shared.Return(buffer, true);

            GraphicsDevice.Textures[1] = _hueSamplers[0];
            GraphicsDevice.Textures[2] = _hueSamplers[1];
            GraphicsDevice.Textures[3] = _hueSamplers[2];

            GumpsLoader.Instance.CreateAtlas(GraphicsDevice);
            LightsLoader.Instance.CreateAtlas(GraphicsDevice);
            AnimationsLoader.Instance.CreateAtlas(GraphicsDevice);

            LightColors.LoadLights();

            // MobileUO: filter mode
            GraphicsDevice.Textures[1].UnityTexture.filterMode = UnityEngine.FilterMode.Point;
            GraphicsDevice.Textures[2].UnityTexture.filterMode = UnityEngine.FilterMode.Point;
            GraphicsDevice.Textures[3].UnityTexture.filterMode = UnityEngine.FilterMode.Point;
            
            // File.WriteAllBytes(Path.Combine(UnityEngine.Application.persistentDataPath, "hue1.png"), UnityEngine.ImageConversion.EncodeToPNG(_hues_sampler[0].UnityTexture as UnityEngine.Texture2D));
            // File.WriteAllBytes(Path.Combine(UnityEngine.Application.persistentDataPath, "hue2.png"), UnityEngine.ImageConversion.EncodeToPNG(_hues_sampler[1].UnityTexture as UnityEngine.Texture2D));

            AnimatedStaticsManager.Initialize();

            GameCursor = new GameCursor();
            Audio = new AudioManager();
            Audio.Initialize();

            SetScene(new LoginScene());
            SetWindowPositionBySettings();
        }

        // MobileUO: makes public
        public override void UnloadContent()
        {
            SDL_GetWindowBordersSize
            (
                Window.Handle,
                out int top,
                out int left,
                out _,
                out _
            );

            Settings.GlobalSettings.WindowPosition = new Point(Math.Max(0, Window.ClientBounds.X - left), Math.Max(0, Window.ClientBounds.Y - top));

            Audio?.StopMusic();
            Settings.GlobalSettings.Save();
            Plugin.OnClosing();

            ArtLoader.Instance.Dispose();
            GumpsLoader.Instance.Dispose();
            TexmapsLoader.Instance.Dispose();
            AnimationsLoader.Instance.Dispose();
            LightsLoader.Instance.Dispose();
            TileDataLoader.Instance.Dispose();
            AnimDataLoader.Instance.Dispose();
            ClilocLoader.Instance.Dispose();
            FontsLoader.Instance.Dispose();
            HuesLoader.Instance.Dispose();
            MapLoader.Instance.Dispose();
            MultiLoader.Instance.Dispose();
            MultiMapLoader.Instance.Dispose();
            ProfessionLoader.Instance.Dispose();
            SkillsLoader.Instance.Dispose();
            SoundsLoader.Instance.Dispose();
            SpeechesLoader.Instance.Dispose();
            Verdata.File?.Dispose();
            World.Map?.Destroy();

            // MobileUO: NOTE: My dispose related changes, see if they're still necessary
            _hueSamplers[0]?.Dispose();
            _hueSamplers[0] = null;
            _hueSamplers[1]?.Dispose();
            _hueSamplers[1] = null;
            Scene?.Dispose();
            AuraManager.Dispose();
            UIManager.Dispose();
            SolidColorTextureCache.Dispose();
            RenderedText.Dispose();
            
            // MobileUO: NOTE: We force the sockets to disconnect in case they haven't already been disposed
            //This is good practice since the Client can be quit while the socket is still active
            if (NetClient.LoginSocket.IsDisposed == false)
            {
                NetClient.LoginSocket.Disconnect();
            }
            if (NetClient.Socket.IsDisposed == false)
            {
                NetClient.Socket.Disconnect();
            }

            base.UnloadContent();
        }


        public void SetWindowTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
#if DEV_BUILD
                Window.Title = $"ClassicUO [dev] - {CUOEnviroment.Version}";
#else
                Window.Title = $"ClassicUO - {CUOEnviroment.Version}";
#endif
            }
            else
            {
#if DEV_BUILD
                Window.Title = $"{title} - ClassicUO [dev] - {CUOEnviroment.Version}";
#else
                Window.Title = $"{title} - ClassicUO - {CUOEnviroment.Version}";
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetScene<T>() where T : Scene
        {
            return Scene as T;
        }

        public void SetScene(Scene scene)
        {
            Scene?.Dispose();
            Scene = scene;

            // MobileUO: NOTE: Added this to be able to react to scene changes, mainly for calculating render scale factor
            Client.InvokeSceneChanged();

            if (scene != null)
            {
                Window.AllowUserResizing = scene.CanResize;
                scene.Load();
            }
        }

        public void SetVSync(bool value)
        {
            GraphicManager.SynchronizeWithVerticalRetrace = value;
        }

        public void SetRefreshRate(int rate)
        {
            if (rate < Constants.MIN_FPS)
            {
                rate = Constants.MIN_FPS;
            }
            else if (rate > Constants.MAX_FPS)
            {
                rate = Constants.MAX_FPS;
            }

            float frameDelay;

            if (rate == Constants.MIN_FPS)
            {
                // The "real" UO framerate is 12.5. Treat "12" as "12.5" to match.
                frameDelay = 80;
            }
            else
            {
                frameDelay = 1000.0f / rate;
            }

            FrameDelay[0] = FrameDelay[1] = (uint) frameDelay;
            FrameDelay[1] = FrameDelay[1] >> 1;

            Settings.GlobalSettings.FPS = rate;

            _intervalFixedUpdate[0] = frameDelay;
            _intervalFixedUpdate[1] = 217; // 5 FPS
        }

        private void SetWindowPosition(int x, int y)
        {
            SDL_SetWindowPosition(Window.Handle, x, y);
        }

        public void SetWindowSize(int width, int height)
        {
            //width = (int) ((double) width * Client.Game.GraphicManager.PreferredBackBufferWidth / Client.Game.Window.ClientBounds.Width);
            //height = (int) ((double) height * Client.Game.GraphicManager.PreferredBackBufferHeight / Client.Game.Window.ClientBounds.Height);

            /*if (CUOEnviroment.IsHighDPI)
            {
                width *= 2;
                height *= 2;
            }
            */

            GraphicManager.PreferredBackBufferWidth = width;
            GraphicManager.PreferredBackBufferHeight = height;
            GraphicManager.ApplyChanges();
        }

        public void SetWindowBorderless(bool borderless)
        {
            SDL_WindowFlags flags = (SDL_WindowFlags) SDL_GetWindowFlags(Window.Handle);

            if ((flags & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) != 0 && borderless)
            {
                return;
            }

            if ((flags & SDL_WindowFlags.SDL_WINDOW_BORDERLESS) == 0 && !borderless)
            {
                return;
            }

            SDL_SetWindowBordered(Window.Handle, borderless ? SDL_bool.SDL_FALSE : SDL_bool.SDL_TRUE);
            SDL_GetCurrentDisplayMode(0, out SDL_DisplayMode displayMode);

            int width = displayMode.w;
            int height = displayMode.h;

            if (borderless)
            {
                SetWindowSize(width, height);
                SDL_SetWindowPosition(Window.Handle, 0, 0);
            }
            else
            {
                SDL_GetWindowBordersSize
                (
                    Window.Handle,
                    out int top,
                    out _,
                    out int bottom,
                    out _
                );

                SetWindowSize(width, height - (top - bottom));
                SetWindowPositionBySettings();
            }

            WorldViewportGump viewport = UIManager.GetGump<WorldViewportGump>();

            if (viewport != null && ProfileManager.CurrentProfile.GameWindowFullSize)
            {
                viewport.ResizeGameWindow(new Point(width, height));
                viewport.X = -5;
                viewport.Y = -5;
            }
        }

        public void MaximizeWindow()
        {
            SDL_MaximizeWindow(Window.Handle);
        }

        public bool IsWindowMaximized()
        {
            SDL_WindowFlags flags = (SDL_WindowFlags) SDL_GetWindowFlags(Window.Handle);

            return (flags & SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0;
        }

        public void RestoreWindow()
        {
            SDL_RestoreWindow(Window.Handle);
        }

        public void SetWindowPositionBySettings()
        {
            SDL_GetWindowBordersSize
            (
                Window.Handle,
                out int top,
                out int left,
                out _,
                out _
            );

            if (Settings.GlobalSettings.WindowPosition.HasValue)
            {
                int x = left + Settings.GlobalSettings.WindowPosition.Value.X;
                int y = top + Settings.GlobalSettings.WindowPosition.Value.Y;
                x = Math.Max(0, x);
                y = Math.Max(0, y);

                SetWindowPosition(x, y);
            }
        }

        protected override void Update(GameTime gameTime)
        {
            if (Profiler.InContext("OutOfContext"))
            {
                Profiler.ExitContext("OutOfContext");
            }

            Time.Ticks = (uint) gameTime.TotalGameTime.TotalMilliseconds;

            // MobileUO: new MouseUpdate function
            // Mouse.Update();
            MouseUpdate();
            OnNetworkUpdate();
            Plugin.Tick();

            if (Scene != null && Scene.IsLoaded && !Scene.IsDestroyed)
            {
                Profiler.EnterContext("Update");
                Scene.Update();
                Profiler.ExitContext("Update");
            }

            // MobileUO: Unity input
            UnityInputUpdate();

            UIManager.Update();

            _totalElapsed += gameTime.ElapsedGameTime.TotalMilliseconds;
            _currentFpsTime += gameTime.ElapsedGameTime.TotalMilliseconds;

            if (_currentFpsTime >= 1000)
            {
                CUOEnviroment.CurrentRefreshRate = _totalFrames;

                _totalFrames = 0;
                _currentFpsTime = 0;
            }

            double x = _intervalFixedUpdate[!IsActive && ProfileManager.CurrentProfile != null && ProfileManager.CurrentProfile.ReduceFPSWhenInactive ? 1 : 0];
            _suppressedDraw = false;

            if (_totalElapsed > x)
            {
                _totalElapsed %= x;
            }
            else
            {
                _suppressedDraw = true;
                SuppressDraw();

                if (!gameTime.IsRunningSlowly)
                {
                    Thread.Sleep(1);
                }
            }

            GameCursor?.Update();
            Audio?.Update();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            Profiler.EndFrame();
            Profiler.BeginFrame();

            if (Profiler.InContext("OutOfContext"))
            {
                Profiler.ExitContext("OutOfContext");
            }

            Profiler.EnterContext("RenderFrame");

            _totalFrames++;

            GraphicsDevice.Clear(Color.Black);

            if (Scene != null && Scene.IsLoaded && !Scene.IsDestroyed)
            {
                Scene.Draw(_uoSpriteBatch);
            }

            UIManager.Draw(_uoSpriteBatch);

            if (World.InGame && SelectedObject.Object is TextObject t)
            {
                if (t.IsTextGump)
                {
                    t.ToTopD();
                }
                else
                {
                    World.WorldTextManager?.MoveToTop(t);
                }
            }

            SelectedObject.HealthbarObject = null;
            SelectedObject.SelectedContainer = null;

            _uoSpriteBatch.Begin();
            GameCursor.Draw(_uoSpriteBatch);
            _uoSpriteBatch.End();

            base.Draw(gameTime);

            Profiler.ExitContext("RenderFrame");
            Profiler.EnterContext("OutOfContext");

            Plugin.ProcessDrawCmdList(GraphicsDevice);
        }

        private void OnNetworkUpdate()
        {
            if (NetClient.LoginSocket.IsDisposed && NetClient.LoginSocket.IsConnected)
            {
                NetClient.LoginSocket.Disconnect();
            }
            else if (!NetClient.Socket.IsConnected)
            {
                NetClient.LoginSocket.Update();
                UpdateSocketStats(NetClient.LoginSocket);
            }
            else if (!NetClient.Socket.IsDisposed)
            {
                NetClient.Socket.Update();
                UpdateSocketStats(NetClient.Socket);
            }
        }

        // MobileUO: commented out
        // MobileUO: TODO: do we need to implement it?
        //protected override bool BeginDraw()
        //{
        //    return !_suppressedDraw && base.BeginDraw();
        //}

        private void UpdateSocketStats(NetClient socket)
        {
            if (_statisticsTimer < Time.Ticks)
            {
                socket.Statistics.Update();
                _statisticsTimer = Time.Ticks + 500;
            }
        }

        private void WindowOnClientSizeChanged(object sender, EventArgs e)
        {
            int width = Window.ClientBounds.Width;
            int height = Window.ClientBounds.Height;

            if (!IsWindowMaximized())
            {
                ProfileManager.CurrentProfile.WindowClientBounds = new Point(width, height);
            }

            SetWindowSize(width, height);

            WorldViewportGump viewport = UIManager.GetGump<WorldViewportGump>();

            if (viewport != null && ProfileManager.CurrentProfile.GameWindowFullSize)
            {
                viewport.ResizeGameWindow(new Point(width, height));
                viewport.X = -5;
                viewport.Y = -5;
            }
        }

        // MobileUO: NOTE: SDL events are not handled in Unity! This function will NOT be hit!
        private int HandleSdlEvent(IntPtr userData, IntPtr ptr)
        {
            SDL_Event* sdlEvent = (SDL_Event*) ptr;

            if (Plugin.ProcessWndProc(sdlEvent) != 0)
            {
                if (sdlEvent->type == SDL_EventType.SDL_MOUSEMOTION)
                {
                    if (GameCursor != null)
                    {
                        GameCursor.AllowDrawSDLCursor = false;
                    }
                }

                return 1;
            }

            switch (sdlEvent->type)
            {
                case SDL_EventType.SDL_AUDIODEVICEADDED:
                    Console.WriteLine("AUDIO ADDED: {0}", sdlEvent->adevice.which);

                    break;

                case SDL_EventType.SDL_AUDIODEVICEREMOVED:
                    Console.WriteLine("AUDIO REMOVED: {0}", sdlEvent->adevice.which);

                    break;

                case SDL_EventType.SDL_WINDOWEVENT:

                    switch (sdlEvent->window.windowEvent)
                    {
                        case SDL_WindowEventID.SDL_WINDOWEVENT_ENTER:
                            Mouse.MouseInWindow = true;

                            break;

                        case SDL_WindowEventID.SDL_WINDOWEVENT_LEAVE:
                            Mouse.MouseInWindow = false;

                            break;

                        case SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED:
                            Plugin.OnFocusGained();

                            break;

                        case SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:
                            Plugin.OnFocusLost();

                            break;
                    }

                    break;

                case SDL_EventType.SDL_KEYDOWN:

                    Keyboard.OnKeyDown(sdlEvent->key);

                    if (Plugin.ProcessHotkeys((int) sdlEvent->key.keysym.sym, (int) sdlEvent->key.keysym.mod, true))
                    {
                        _ignoreNextTextInput = false;

                        UIManager.KeyboardFocusControl?.InvokeKeyDown(sdlEvent->key.keysym.sym, sdlEvent->key.keysym.mod);

                        Scene.OnKeyDown(sdlEvent->key);
                    }
                    else
                    {
                        _ignoreNextTextInput = true;
                    }

                    break;

                case SDL_EventType.SDL_KEYUP:

                    Keyboard.OnKeyUp(sdlEvent->key);
                    UIManager.KeyboardFocusControl?.InvokeKeyUp(sdlEvent->key.keysym.sym, sdlEvent->key.keysym.mod);
                    Scene.OnKeyUp(sdlEvent->key);
                    Plugin.ProcessHotkeys(0, 0, false);

                    if (sdlEvent->key.keysym.sym == SDL_Keycode.SDLK_PRINTSCREEN)
                    {
                        // MobileUO: commented out
                        // TakeScreenshot();
                    }
                    
                    break;

                case SDL_EventType.SDL_TEXTINPUT:

                    if (_ignoreNextTextInput)
                    {
                        break;
                    }

                    // Fix for linux OS: https://github.com/andreakarasho/ClassicUO/pull/1263
                    // Fix 2: SDL owns this behaviour. Cheating is not a real solution.
                    /*if (!Utility.Platforms.PlatformHelper.IsWindows)
                    {
                        if (Keyboard.Alt || Keyboard.Ctrl)
                        {
                            break;
                        }
                    }*/

                    string s = UTF8_ToManaged((IntPtr) sdlEvent->text.text, false);

                    if (!string.IsNullOrEmpty(s))
                    {
                        UIManager.KeyboardFocusControl?.InvokeTextInput(s);
                        Scene.OnTextInput(s);
                    }

                    break;

                case SDL_EventType.SDL_MOUSEMOTION:

                    if (GameCursor != null && !GameCursor.AllowDrawSDLCursor)
                    {
                        GameCursor.AllowDrawSDLCursor = true;
                        GameCursor.Graphic = 0xFFFF;
                    }

                    Mouse.Update();

                    if (Mouse.IsDragging)
                    {
                        if (!Scene.OnMouseDragging())
                        {
                            UIManager.OnMouseDragging();
                        }
                    }

                    break;

                case SDL_EventType.SDL_MOUSEWHEEL:
                    Mouse.Update();
                    bool isScrolledUp = sdlEvent->wheel.y > 0;

                    Plugin.ProcessMouse(0, sdlEvent->wheel.y);

                    if (!Scene.OnMouseWheel(isScrolledUp))
                    {
                        UIManager.OnMouseWheel(isScrolledUp);
                    }

                    break;

                case SDL_EventType.SDL_MOUSEBUTTONDOWN:
                {
                    SDL_MouseButtonEvent mouse = sdlEvent->button;

                    // The values in MouseButtonType are chosen to exactly match the SDL values
                    MouseButtonType buttonType = (MouseButtonType) mouse.button;

                    uint lastClickTime = 0;

                    switch (buttonType)
                    {
                        case MouseButtonType.Left:
                            lastClickTime = Mouse.LastLeftButtonClickTime;

                            break;

                        case MouseButtonType.Middle:
                            lastClickTime = Mouse.LastMidButtonClickTime;

                            break;

                        case MouseButtonType.Right:
                            lastClickTime = Mouse.LastRightButtonClickTime;

                            break;

                        default: 
                            Log.Warn($"No mouse button handled: {mouse.button}");

                            break;
                    }

                    Mouse.ButtonPress(buttonType);
                    Mouse.Update();

                    uint ticks = Time.Ticks;

                    if (lastClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK >= ticks)
                    {
                        lastClickTime = 0;

                        bool res = Scene.OnMouseDoubleClick(buttonType) || UIManager.OnMouseDoubleClick(buttonType);

                        if (!res)
                        {
                            if (!Scene.OnMouseDown(buttonType))
                            {
                                UIManager.OnMouseButtonDown(buttonType);
                            }
                        }
                        else
                        {
                            lastClickTime = 0xFFFF_FFFF;
                        }
                    }
                    else
                    {
                        if (buttonType != MouseButtonType.Left && buttonType != MouseButtonType.Right)
                        {
                            Plugin.ProcessMouse(sdlEvent->button.button, 0);
                        }

                        if (!Scene.OnMouseDown(buttonType))
                        {
                            UIManager.OnMouseButtonDown(buttonType);
                        }

                        lastClickTime = Mouse.CancelDoubleClick ? 0 : ticks;
                    }

                    switch (buttonType)
                    {
                        case MouseButtonType.Left:
                            Mouse.LastLeftButtonClickTime = lastClickTime;

                            break;

                        case MouseButtonType.Middle:
                            Mouse.LastMidButtonClickTime = lastClickTime;

                            break;

                        case MouseButtonType.Right:
                            Mouse.LastRightButtonClickTime = lastClickTime;

                            break;
                    }

                    break;
                }

                case SDL_EventType.SDL_MOUSEBUTTONUP:
                {
                    SDL_MouseButtonEvent mouse = sdlEvent->button;

                    // The values in MouseButtonType are chosen to exactly match the SDL values
                    MouseButtonType buttonType = (MouseButtonType) mouse.button;

                    uint lastClickTime = 0;

                    switch (buttonType)
                    {
                        case MouseButtonType.Left:
                            lastClickTime = Mouse.LastLeftButtonClickTime;

                            break;

                        case MouseButtonType.Middle:
                            lastClickTime = Mouse.LastMidButtonClickTime;

                            break;

                        case MouseButtonType.Right:
                            lastClickTime = Mouse.LastRightButtonClickTime;

                            break;

                        default:
                            Log.Warn($"No mouse button handled: {mouse.button}");

                            break;
                        }

                    if (lastClickTime != 0xFFFF_FFFF)
                    {
                        if (!Scene.OnMouseUp(buttonType) || UIManager.LastControlMouseDown(buttonType) != null)
                        {
                            UIManager.OnMouseButtonUp(buttonType);
                        }
                    }

                    Mouse.ButtonRelease(buttonType);
                    Mouse.Update();

                    break;
                }
            }

            return 1;
        }

        // MobileUO: commented out
        //private void TakeScreenshot()
        //{
        //    string screenshotsFolder = FileSystemHelper.CreateFolderIfNotExists
        //        (CUOEnviroment.ExecutablePath, "Data", "Client", "Screenshots");

        //    string path = Path.Combine(screenshotsFolder, $"screenshot_{DateTime.Now:yyyy-MM-dd_hh-mm-ss}.png");

        //    Color[] colors =
        //        new Color[GraphicManager.PreferredBackBufferWidth * GraphicManager.PreferredBackBufferHeight];

        //    GraphicsDevice.GetBackBufferData(colors);

        //    using (Texture2D texture = new Texture2D
        //    (
        //        GraphicsDevice, GraphicManager.PreferredBackBufferWidth, GraphicManager.PreferredBackBufferHeight,
        //        false, SurfaceFormat.Color
        //    ))
        //    using (FileStream fileStream = File.Create(path))
        //    {
        //        texture.SetData(colors);
        //        texture.SaveAsPng(fileStream, texture.Width, texture.Height);
        //        string message = string.Format(ResGeneral.ScreenshotStoredIn0, path);

        //        if (ProfileManager.CurrentProfile == null || ProfileManager.CurrentProfile.HideScreenshotStoredInMessage)
        //        {
        //            Log.Info(message);
        //        }
        //        else
        //        {
        //            GameActions.Print(message, 0x44, MessageType.System);
        //        }
        //    }
        //}

        // MobileUO: here to end of file for Unity functions to help support inputs
        private readonly UnityEngine.KeyCode[] _keyCodeEnumValues = (UnityEngine.KeyCode[]) Enum.GetValues(typeof(UnityEngine.KeyCode));
        private UnityEngine.Vector3 lastMousePosition;
        public SDL_Keymod KeymodOverride;
        public bool EscOverride;
        private int zoomCounter;

        private void MouseUpdate()
        {
            var oneOverScale = 1f / Batcher.scale;
            
            //Finger/mouse handling
            if (UnityEngine.Application.isMobilePlatform && UserPreferences.UseMouseOnMobile.CurrentValue == 0)
            {
                var fingers = Lean.Touch.LeanTouch.GetFingers(true, false);

                //Only process one finger that has not started over gui because using multiple fingers with UIManager
                //causes issues due to the assumption that there's only one pointer, such as on finger "stealing"
                //a dragged gump from another
                if (fingers.Count > 0)
                {
                    var finger = fingers[0];
                    
                    var leftMouseDown = finger.Down;
                    var leftMouseHeld = finger.Set;

                    var mousePositionPoint = ConvertUnityMousePosition(finger.ScreenPosition, oneOverScale);
                    Mouse.Position = mousePositionPoint;
                    Mouse.LButtonPressed = leftMouseDown || leftMouseHeld;
                    Mouse.RButtonPressed = false;
                    Mouse.IsDragging = Mouse.LButtonPressed || Mouse.RButtonPressed;
                    //Mouse.RealPosition = Mouse.Position;
                }
            }
            else
            {
                var leftMouseDown = UnityEngine.Input.GetMouseButtonDown(0);
                var leftMouseHeld = UnityEngine.Input.GetMouseButton(0);
                var rightMouseDown = UnityEngine.Input.GetMouseButtonDown(1);
                var rightMouseHeld = UnityEngine.Input.GetMouseButton(1);
                var mousePosition = UnityEngine.Input.mousePosition;

                if (Lean.Touch.LeanTouch.PointOverGui(mousePosition))
                {
                    Mouse.Position.X = 0;
                    Mouse.Position.Y = 0;
                    leftMouseDown = false;
                    leftMouseHeld = false;
                    rightMouseDown = false;
                    rightMouseHeld = false;
                }
                
                var mousePositionPoint = ConvertUnityMousePosition(mousePosition, oneOverScale);
                Mouse.Position = mousePositionPoint;
                Mouse.LButtonPressed = leftMouseDown || leftMouseHeld;
                Mouse.RButtonPressed = rightMouseDown || rightMouseHeld;
                Mouse.IsDragging = Mouse.LButtonPressed || Mouse.RButtonPressed;
                //Mouse.RealPosition = Mouse.Position;
            }
        }

        private void UnityInputUpdate()
        {
            var oneOverScale = 1f / Batcher.scale;
            
            //Finger/mouse handling
            if (UnityEngine.Application.isMobilePlatform && UserPreferences.UseMouseOnMobile.CurrentValue == 0)
            {
                var fingers = Lean.Touch.LeanTouch.GetFingers(true, false);

                //Detect two finger tap gesture for closing gumps, only when one of the fingers' state is Down
                if (fingers.Count == 2 && (fingers[0].Down || fingers[1].Down))
                {
                    var firstMousePositionPoint = ConvertUnityMousePosition(fingers[0].ScreenPosition, oneOverScale);
                    var secondMousePositionPoint = ConvertUnityMousePosition(fingers[1].ScreenPosition, oneOverScale);
                    var firstControlUnderFinger = UIManager.GetMouseOverControl(firstMousePositionPoint);
                    var secondControlUnderFinger = UIManager.GetMouseOverControl(secondMousePositionPoint);
                    //We prefer to get the root parent but sometimes it can be null (like with GridLootGump), in which case we revert to the initially found control
                    firstControlUnderFinger = firstControlUnderFinger?.RootParent ?? firstControlUnderFinger;
                    secondControlUnderFinger = secondControlUnderFinger?.RootParent ?? secondControlUnderFinger;
                    if (firstControlUnderFinger != null && firstControlUnderFinger == secondControlUnderFinger)
                    {
                        //Simulate right mouse down and up
                        SimulateMouse(false, false, true, false, false, true);
                        SimulateMouse(false, false, false, true, false, true);
                    }
                }
                //Only process one finger that has not started over gui because using multiple fingers with UIManager
                //causes issues due to the assumption that there's only one pointer, such as one finger "stealing" a
                //dragged gump from another
                else if (fingers.Count > 0)
                {
                    var finger = fingers[0];
                    var mouseMotion = finger.ScreenPosition != finger.LastScreenPosition;
                    SimulateMouse(finger.Down, finger.Up, false, false, mouseMotion, false);
                }
                
                if (fingers.Count == 2 && ProfileManager.CurrentProfile.EnableMousewheelScaleZoom && UIManager.IsMouseOverWorld)
                {                    
                    var scale = Lean.Touch.LeanGesture.GetPinchScale(fingers);                  
                    if(scale < 1)
                    {
                        zoomCounter--;
                    }
                    else if(scale > 1)
                    {
                        zoomCounter++;
                    }

                    if(zoomCounter > 3)
                    {
                        zoomCounter = 0;
                        --Client.Game.Scene.Camera.ZoomIndex;
                    }
                    else if(zoomCounter < -3)
                    {
                        zoomCounter = 0;
                        ++Client.Game.Scene.Camera.ZoomIndex;
                    }
                }

            }
            else
            {
                var leftMouseDown = UnityEngine.Input.GetMouseButtonDown(0);
                var leftMouseUp = UnityEngine.Input.GetMouseButtonUp(0);
                var rightMouseDown = UnityEngine.Input.GetMouseButtonDown(1);
                var rightMouseUp = UnityEngine.Input.GetMouseButtonUp(1);
                var mousePosition = UnityEngine.Input.mousePosition;
                var mouseMotion = mousePosition != lastMousePosition;
                lastMousePosition = mousePosition;
                
                if (Lean.Touch.LeanTouch.PointOverGui(mousePosition))
                {
                    Mouse.Position.X = 0;
                    Mouse.Position.Y = 0;
                    leftMouseDown = false;
                    leftMouseUp = false;
                    rightMouseDown = false;
                    rightMouseUp = false;
                }
                
                SimulateMouse(leftMouseDown, leftMouseUp, rightMouseDown, rightMouseUp, mouseMotion, false);
            }

            //Keyboard handling
            var keymod = KeymodOverride;
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftAlt))
            {
                keymod |= SDL_Keymod.KMOD_LALT;
            }
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightAlt))
            {
                keymod |= SDL_Keymod.KMOD_RALT;
            }
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift))
            {
                keymod |= SDL_Keymod.KMOD_LSHIFT;
            }
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift))
            {
                keymod |= SDL_Keymod.KMOD_RSHIFT;
            }
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl))
            {
                keymod |= SDL_Keymod.KMOD_LCTRL;
            }
            if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl))
            {
                keymod |= SDL_Keymod.KMOD_RCTRL;
            }
            
            Keyboard.Shift = (keymod & SDL_Keymod.KMOD_SHIFT) != SDL_Keymod.KMOD_NONE;
            Keyboard.Alt = (keymod & SDL_Keymod.KMOD_ALT) != SDL_Keymod.KMOD_NONE;
            Keyboard.Ctrl = (keymod & SDL_Keymod.KMOD_CTRL) != SDL_Keymod.KMOD_NONE;
            
            foreach (var keyCode in _keyCodeEnumValues)
            {
                var key = new SDL_KeyboardEvent {keysym = new SDL_Keysym {sym = (SDL_Keycode) keyCode, mod = keymod}};
                if (UnityEngine.Input.GetKeyDown(keyCode))
                {
                    Keyboard.OnKeyDown(key);

                    if (Plugin.ProcessHotkeys((int) key.keysym.sym, (int) key.keysym.mod, true))
                    {
                        _ignoreNextTextInput = false;
                        UIManager.KeyboardFocusControl?.InvokeKeyDown(key.keysym.sym, key.keysym.mod);
                        Scene.OnKeyDown(key);
                    }
                    else
                        _ignoreNextTextInput = true;
                }
                if (UnityEngine.Input.GetKeyUp(keyCode))
                {
                    Keyboard.OnKeyUp(key);
                    UIManager.KeyboardFocusControl?.InvokeKeyUp(key.keysym.sym, key.keysym.mod);
                    Scene.OnKeyUp(key);
                    Plugin.ProcessHotkeys(0, 0, false);
                }
            }

            if (EscOverride)
            {
                EscOverride = false;
                var key = new SDL_KeyboardEvent {keysym = new SDL_Keysym {sym = (SDL_Keycode) UnityEngine.KeyCode.Escape, mod = keymod}};
                // if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                {
                    Keyboard.OnKeyDown(key);

                    if (Plugin.ProcessHotkeys((int) key.keysym.sym, (int) key.keysym.mod, true))
                    {
                        _ignoreNextTextInput = false;
                        UIManager.KeyboardFocusControl?.InvokeKeyDown(key.keysym.sym, key.keysym.mod);
                        Scene.OnKeyDown(key);
                    }
                    else
                        _ignoreNextTextInput = true;
                }
                // if (UnityEngine.Input.GetKeyUp(KeyCode.Escape))
                {
                    Keyboard.OnKeyUp(key);
                    UIManager.KeyboardFocusControl?.InvokeKeyUp(key.keysym.sym, key.keysym.mod);
                    Scene.OnKeyUp(key);
                    Plugin.ProcessHotkeys(0, 0, false);
                }
            }

            //Input text handling
            if (UnityEngine.Application.isMobilePlatform && TouchScreenKeyboard != null)
            {
                var text = TouchScreenKeyboard.text;
                
                if (_ignoreNextTextInput == false && TouchScreenKeyboard.status == UnityEngine.TouchScreenKeyboard.Status.Done)
                {
                    //Clear the text of TouchScreenKeyboard, otherwise it stays there and is re-evaluated every frame
                    TouchScreenKeyboard.text = string.Empty;
                    
                    //Set keyboard to null so we process its text only once when its status is set to Done
                    TouchScreenKeyboard = null;
                    
                    //Need to clear the existing text in textbox before "pasting" new text from TouchScreenKeyboard
                    if (UIManager.KeyboardFocusControl is StbTextBox stbTextBox)
                    {
                        stbTextBox.SetText(string.Empty);
                    }
                    
                    UIManager.KeyboardFocusControl?.InvokeTextInput(text);
                    Scene.OnTextInput(text);
                    
                    //When targeting SystemChat textbox, "auto-press" return key so that the text entered on the TouchScreenKeyboard is submitted right away
                    if (UIManager.KeyboardFocusControl != null && UIManager.KeyboardFocusControl == UIManager.SystemChat?.TextBoxControl)
                    {
                        //Handle different chat modes
                        HandleChatMode(text);
                        //"Press" return
                        UIManager.KeyboardFocusControl.InvokeKeyDown(SDL_Keycode.SDLK_RETURN, SDL_Keymod.KMOD_NONE);
                        //Revert chat mode to default
                        UIManager.SystemChat.Mode = ChatMode.Default;
                    }
                }
            }
            else
            {
                var text = UnityEngine.Input.inputString;
                //Backspace character should not be sent as text input
                text = text.Replace("\b", "");
                if (_ignoreNextTextInput == false && string.IsNullOrEmpty(text) == false)
                {
                    UIManager.KeyboardFocusControl?.InvokeTextInput(text);
                    Scene.OnTextInput(text);
                }
            }
        }

        private void HandleChatMode(string text)
        {
            if (text.Length > 0)
            {
                switch (text[0])
                {                  
                    case '/':
                        UIManager.SystemChat.Mode = ChatMode.Party;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(1));
                        break;
                    case '\\':
                        UIManager.SystemChat.Mode = ChatMode.Guild;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(1));
                        break;
                    case '|':
                        UIManager.SystemChat.Mode = ChatMode.Alliance;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(1));
                        break;
                    case '-':
                        UIManager.SystemChat.Mode = ChatMode.ClientCommand;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(1));
                        break;
                    case ',' when ChatManager.ChatIsEnabled == ChatStatus.Enabled:
                        UIManager.SystemChat.Mode = ChatMode.UOChat;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(1));
                        break;
                    case ':' when text.Length > 1 && text[1] == ' ':
                        UIManager.SystemChat.Mode = ChatMode.Emote;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(2));
                        break;
                    case ';' when text.Length > 1 && text[1] == ' ':
                        UIManager.SystemChat.Mode = ChatMode.Whisper;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(2));
                        break;
                    case '!' when text.Length > 1 && text[1] == ' ':
                        UIManager.SystemChat.Mode = ChatMode.Yell;
                        //Textbox text has been cleared, set it again
                        UIManager.SystemChat.TextBoxControl.InvokeTextInput(text.Substring(2));
                        break;
                }
            }
        }

        private static Point ConvertUnityMousePosition(UnityEngine.Vector2 screenPosition, float oneOverScale)
        {
            var x = UnityEngine.Mathf.RoundToInt(screenPosition.x * oneOverScale);
            var y = UnityEngine.Mathf.RoundToInt((UnityEngine.Screen.height - screenPosition.y) * oneOverScale);
            return new Point(x, y);
        }

        private void SimulateMouse(bool leftMouseDown, bool leftMouseUp, bool rightMouseDown, bool rightMouseUp, bool mouseMotion, bool skipSceneInput)
        {
            // MobileUO: TODO: do we need to bring this back?
            //if (_dragStarted && !Mouse.LButtonPressed)
            //{
            //    _dragStarted = false;
            //}
            
            if (leftMouseDown)
            {
                Mouse.LClickPosition = Mouse.Position;
                Mouse.CancelDoubleClick = false;
                uint ticks = Time.Ticks;
                if (Mouse.LastLeftButtonClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK >= ticks)
                {
                    Mouse.LastLeftButtonClickTime = 0;

                    var res = false;
                    if (skipSceneInput)
                    {
                        res = UIManager.OnMouseDoubleClick(MouseButtonType.Left);
                    }
                    else
                    {
                        res = Scene.OnMouseDoubleClick(MouseButtonType.Left) || UIManager.OnMouseDoubleClick(MouseButtonType.Left);
                    }

                    if (!res)
                    {
                        if (skipSceneInput || !Scene.OnMouseDown(MouseButtonType.Left))
                            UIManager.OnMouseButtonDown(MouseButtonType.Left);
                    }
                    else
                    {
                        Mouse.LastLeftButtonClickTime = 0xFFFF_FFFF;
                    }
                }
                else
                {
                    if (skipSceneInput || !Scene.OnMouseDown(MouseButtonType.Left))
                        UIManager.OnMouseButtonDown(MouseButtonType.Left);
                    Mouse.LastLeftButtonClickTime = Mouse.CancelDoubleClick ? 0 : ticks;
                }
            }
            else if (leftMouseUp)
            {
                if (Mouse.LastLeftButtonClickTime != 0xFFFF_FFFF)
                {
                    if (skipSceneInput || !Scene.OnMouseUp(MouseButtonType.Left) || UIManager.LastControlMouseDown(MouseButtonType.Left) != null)
                        UIManager.OnMouseButtonUp(MouseButtonType.Left);
                }

                //Mouse.End();
            }

            if (rightMouseDown)
            {
                Mouse.RClickPosition = Mouse.Position;
                Mouse.CancelDoubleClick = false;
                uint ticks = Time.Ticks;

                if (Mouse.LastRightButtonClickTime + Mouse.MOUSE_DELAY_DOUBLE_CLICK >= ticks)
                {
                    Mouse.LastRightButtonClickTime = 0;

                    var res = false;
                    if (skipSceneInput)
                    {
                        res = UIManager.OnMouseDoubleClick(MouseButtonType.Right);
                    }
                    else
                    {
                        res = Scene.OnMouseDoubleClick(MouseButtonType.Right) || UIManager.OnMouseDoubleClick(MouseButtonType.Right);
                    }
                    
                    if (!res)
                    {
                        if (skipSceneInput || !Scene.OnMouseDown(MouseButtonType.Right))
                            UIManager.OnMouseButtonDown(MouseButtonType.Right);
                    }
                    else
                    {
                        Mouse.LastRightButtonClickTime = 0xFFFF_FFFF;
                    }
                }
                else
                {
                    if (skipSceneInput || !Scene.OnMouseDown(MouseButtonType.Right))
                        UIManager.OnMouseButtonDown(MouseButtonType.Right);
                    Mouse.LastRightButtonClickTime = Mouse.CancelDoubleClick ? 0 : ticks;
                }
            }
            else if (rightMouseUp)
            {
                if (Mouse.LastRightButtonClickTime != 0xFFFF_FFFF)
                {
                    if (skipSceneInput || !Scene.OnMouseUp(MouseButtonType.Right))
                        UIManager.OnMouseButtonUp(MouseButtonType.Right);
                }

                //Mouse.End();
            }

            if (mouseMotion)
            {
                if (Mouse.IsDragging)
                {
                    if (skipSceneInput || !Scene.OnMouseDragging())
                        UIManager.OnMouseDragging();
                }

                // MobileUO: TODO: do we need to bring this back?
                //if (Mouse.IsDragging && !_dragStarted)
                //{
                //    _dragStarted = true;
                //}
            }
        }
    }
}