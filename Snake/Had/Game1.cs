using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Had
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // Grid
        private const int CellSize = 32;
        private int _gridWidth = 20;
        private int _gridHeight = 15;

        // store initial sizes so we can reset on death
        private readonly int _initialBackBufferWidth;
        private readonly int _initialBackBufferHeight;

        // Snake
        private readonly List<Point> _snake = new();
        private Point _direction = new(1, 0);
        private double _moveTimer;
        private const double MoveInterval = 0.12; // seconds
        private bool _growNextMove;

        // Cherry (was apple)
        private Point _cherry;
        private readonly Random _rng = new();

        // Score
        private int _score;
        // Deaths
        private int _deaths;

        // Rendering helpers
        private Texture2D _pixel = null!;
        private readonly Color _snakeFill = Color.White;
        private readonly Color _snakeOutline = new Color(255, 128, 200); // pink
        private readonly Color _cherryColor = Color.Red; // renamed from _appleColor
        private readonly Color _tailTrailColor = new Color(100, 255, 140);
        private readonly Color _scoreColor = Color.White;
        private readonly Vector2 _scorePosition = new(8, 8);

        // Death / pause snapshot
        private bool _isDead = false;
        private bool _takeDeathSnapshot = false;
        private RenderTarget2D? _deathSnapshot = null;
        private float _deathZoomTimer = 0f;
        private const float DeathZoomDuration = 0.8f; // time to zoom in
        private const float DeathZoomAmount = 1.12f; // final zoom multiplier

        // You died text animation
        private float _deathPopTimer = 0f;
        private const float DeathPopDuration = 0.6f;
        private float _deathIdleAnimTime = 0f;
        private float _deathPhase = 0f;

        // dismissal (when pressing space) swoosh
        private float _deathDismissTimer = 0f;
        private const float DeathDismissDuration = 0.35f;

        // Particles: split into background (drawn first) and foreground
        private readonly List<Particle> _bgParticles = new();
        private readonly List<Particle> _fgParticles = new();
        // cap total
        private const int MaxParticles = 8000;

        // Cherry particle timing
        private float _cherryParticleTimer = 0f;
        private const float CherryParticleDuration = 2.2f; // seconds of burst after pickup

        // Background emitter enabled at or above this score
        private const int BackgroundEnableScore = 5;

        // Streak and screenshake
        private int _streak = 0; // consecutive quick pickups
        private float _timeSinceLastCherry = 100f;
        private const float StreakTimeout = 3.5f; // time to break streak
        private int _firstCherryBurstCount = 48; // baseline burst count when starting a streak
        private const int MaxStreak = 6; // cap streak
        private const int MaxBurstMultiplier = 6; // cap multiplier for burst size

        // screen shake
        private float _shakeTimer = 0f;
        private float _shakeDuration = 0.45f;
        private float _shakeAmplitude = 0f;
        private const float MaxShakeAmplitude = 12f;

        // Faces
        private readonly List<string> _faces = new() { ":3", "^w^", "˃ w ˂", "UwU", "0w0", "(w)", "n_n" };
        private int _currentFaceIndex = 0;
        private double _faceTimer = 0.0;
        private const double FaceInterval = 0.7; // seconds between face changes

        // --- Streak display (single big number) ---
        private int _streakDisplayNumber = 0; // 0 = none, otherwise 1..MaxStreak
        // pop animation when a cherry is picked
        private float _streakPopTimer = 0f;
        private const float StreakPopDuration = 0.5f; // short pop animation
        // fade when streak runs out
        private float _streakFadeTimer = 0f;
        private const float StreakFadeDuration = 0.6f;
        // idle animation so the big number feels alive while sitting
        private float _streakIdleAnimTime = 0f;
        private float _streakPhase = 0f;
        private const float StreakIdleSpeed = 1.2f;
        private const float StreakIdleAmplitude = 20f;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            // initial grid size
            _gridWidth = 20;
            _gridHeight = 15;

            // store initial pixel sizes so we can restore on death
            _initialBackBufferWidth = _gridWidth * CellSize;
            _initialBackBufferHeight = _gridHeight * CellSize;

            // set window size based on grid
            _graphics.PreferredBackBufferWidth = _initialBackBufferWidth;
            _graphics.PreferredBackBufferHeight = _initialBackBufferHeight;
            _graphics.ApplyChanges();
        }

        protected override void Initialize()
        {
            // reset window back to initial in case this Initialize is called on death
            if (_graphics.PreferredBackBufferWidth != _initialBackBufferWidth || _graphics.PreferredBackBufferHeight != _initialBackBufferHeight)
            {
                _graphics.PreferredBackBufferWidth = _initialBackBufferWidth;
                _graphics.PreferredBackBufferHeight = _initialBackBufferHeight;
                _graphics.ApplyChanges();
            }

            // ensure grid matches window
            _gridWidth = _graphics.PreferredBackBufferWidth / CellSize;
            _gridHeight = _graphics.PreferredBackBufferHeight / CellSize;

            // Initialize snake in center
            _snake.Clear();
            var start = new Point(_gridWidth / 2, _gridHeight / 2);
            _snake.Add(start);
            _snake.Add(new Point(start.X - 1, start.Y));
            _snake.Add(new Point(start.X - 2, start.Y));
            _direction = new Point(1, 0);
            _moveTimer = 0;
            _growNextMove = false;

            // start with zero particles
            _bgParticles.Clear();
            _fgParticles.Clear();
            _cherryParticleTimer = 0f;
            _streak = 0;
            _timeSinceLastCherry = 100f;
            _deaths = 0;

            // reset streak display
            _streakDisplayNumber = 0;
            _streakPopTimer = 0f;
            _streakFadeTimer = 0f;
            _streakIdleAnimTime = 0f;
            _streakPhase = (float)(_rng.NextDouble() * Math.PI * 2.0);

            PlaceCherry();

            base.Initialize();
        }

        // Resets window and game state when player dies (or on start)
        private void ResetGame()
        {
            // restore window size to initial
            if (_graphics.PreferredBackBufferWidth != _initialBackBufferWidth || _graphics.PreferredBackBufferHeight != _initialBackBufferHeight)
            {
                _graphics.PreferredBackBufferWidth = _initialBackBufferWidth;
                _graphics.PreferredBackBufferHeight = _initialBackBufferHeight;
                _graphics.ApplyChanges();
            }

            // recompute grid size
            _gridWidth = Math.Max(1, _graphics.PreferredBackBufferWidth / CellSize);
            _gridHeight = Math.Max(1, _graphics.PreferredBackBufferHeight / CellSize);

            // reset snake in center
            _snake.Clear();
            var start = new Point(_gridWidth / 2, _gridHeight / 2);
            _snake.Add(start);
            _snake.Add(new Point(start.X - 1, start.Y));
            _snake.Add(new Point(start.X - 2, start.Y));
            _direction = new Point(1, 0);
            _moveTimer = 0;
            _growNextMove = false;

            // clear particles so they don't linger across size changes
            _bgParticles.Clear();
            _fgParticles.Clear();
            _cherryParticleTimer = 0f;
            _streak = 0;
            _timeSinceLastCherry = 100f;

            // reset streak display
            _streakDisplayNumber = 0;
            _streakPopTimer = 0f;
            _streakFadeTimer = 0f;
            _streakIdleAnimTime = 0f;
            _streakPhase = (float)(_rng.NextDouble() * Math.PI * 2.0);

            PlaceCherry();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // 1x1 pixel texture used for rectangles and pixel font
            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }

        protected override void Update(GameTime gameTime)
        {
            // Exit
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed || Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            HandleInput();

            var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // if dead, only advance death timers & handle input for restart
            if (_isDead)
            {
                // advance zoom timer
                if (_deathZoomTimer < DeathZoomDuration)
                {
                    _deathZoomTimer += dt;
                    if (_deathZoomTimer > DeathZoomDuration) _deathZoomTimer = DeathZoomDuration;
                }

                // pop animation countdown
                if (_deathPopTimer > 0f)
                {
                    _deathPopTimer -= dt;
                    if (_deathPopTimer < 0f) _deathPopTimer = 0f;
                }
                else
                {
                    _deathIdleAnimTime += dt;
                }

                // dismissal animation
                if (_deathDismissTimer > 0f)
                {
                    _deathDismissTimer -= dt;
                    if (_deathDismissTimer <= 0f)
                    {
                        // finish dismissal -> restart
                        _deathDismissTimer = 0f;
                        _isDead = false;
                        // clear snapshot
                        if (_deathSnapshot != null)
                        {
                            _deathSnapshot.Dispose();
                            _deathSnapshot = null;
                        }
                        // actually reset game state for a fresh playthrough
                        _score = 0;
                        ResetGame();
                    }
                }

                // check for space to start dismissal (only if not already dismissing)
                var kb = Keyboard.GetState();
                if (kb.IsKeyDown(Keys.Space) && _deathDismissTimer <= 0f)
                {
                    _deathDismissTimer = DeathDismissDuration;
                }

                // while dead, skip further game updates
                base.Update(gameTime);
                return;
            }

            // determine current move interval (faster when streak >= 3 and streak is active)
            double currentMoveInterval = MoveInterval;
            bool speedBoostActive = (_streak >= 3 && _timeSinceLastCherry <= StreakTimeout);
            if (speedBoostActive)
            {
                // 1.5x faster -> interval divided by 1.5
                currentMoveInterval = MoveInterval / 1.5;
            }

            _moveTimer += dt;
            if (_moveTimer >= currentMoveInterval)
            {
                _moveTimer -= currentMoveInterval;
                MoveSnake();
            }

            // Face rotation timer
            _faceTimer += dt;
            if (_faceTimer >= FaceInterval)
            {
                _faceTimer -= FaceInterval;
                _currentFaceIndex = (_currentFaceIndex + 1) % _faces.Count;
            }

            // Update both particle lists
            UpdateParticleList(_bgParticles, dt);
            UpdateParticleList(_fgParticles, dt);

            // advance streak timer
            _timeSinceLastCherry += dt;
            if (_timeSinceLastCherry > StreakTimeout)
            {
                // break streak if timed out
                _streak = 0;
                // begin fade if we were showing a number
                if (_streakDisplayNumber > 0 && _streakFadeTimer <= 0f)
                {
                    _streakFadeTimer = StreakFadeDuration;
                }
            }

            // update shake timer
            if (_shakeTimer > 0f)
            {
                _shakeTimer -= dt;
                if (_shakeTimer <= 0f) { _shakeTimer = 0f; _shakeAmplitude = 0f; }
            }

            // If we have an active cherry burst timer, emit particles around the snake head for a few seconds
            if (_cherryParticleTimer > 0f)
            {
                _cherryParticleTimer -= dt;
                EmitAroundSnake(dt);
            }

            // If the temporary speed boost is active (streak >= 3 and streak timer running), emit green tail particles
            if (speedBoostActive)
            {
                EmitTailTrail(dt);
            }

            // If score >= threshold, enable a low-intensity persistent background emitter
            if (_score >= BackgroundEnableScore)
            {
                // spawn a few background particles per frame (low intensity)
                int bgSpawn = 1 + (_score - BackgroundEnableScore) / 2; // slowly increase with score
                for (int i = 0; i < bgSpawn && (_bgParticles.Count + _fgParticles.Count) < MaxParticles; i++)
                {
                    float px = (float)(_rng.NextDouble() * Math.Max(1, _graphics.PreferredBackBufferWidth));
                    float py = (float)(_rng.NextDouble() * Math.Max(1, _graphics.PreferredBackBufferHeight));
                    var pos = new Vector2(px, py);
                    var vel = new Vector2((float)(_rng.NextDouble() * 40 - 20), (float)(_rng.NextDouble() * 40 - 20));
                    float life = 6f + (float)(_rng.NextDouble() * 6f);
                    float size = (float)(_rng.NextDouble() * 8 + 2) * 0.6f; // smaller background
                    SpawnParticle(pos, vel, life, _snakeOutline * 0.6f, size, background: true, persistent: false);
                }
            }

            // Update pop & fade timers for streak display
            if (_streakPopTimer > 0f)
            {
                _streakPopTimer -= dt;
                if (_streakPopTimer < 0f) _streakPopTimer = 0f;
            }

            if (_streakFadeTimer > 0f)
            {
                _streakFadeTimer -= dt;
                if (_streakFadeTimer <= 0f)
                {
                    // finished fading out
                    _streakFadeTimer = 0f;
                    _streakDisplayNumber = 0;
                }
            }

            // idle animation time advances while a streak number is shown
            if (_streakDisplayNumber > 0 && _streakFadeTimer <= 0f)
            {
                _streakIdleAnimTime += dt * StreakIdleSpeed;
            }

            base.Update(gameTime);
        }

        private void HandleInput()
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Up) && _direction.Y != 1) _direction = new Point(0, -1);
            if (kb.IsKeyDown(Keys.Down) && _direction.Y != -1) _direction = new Point(0, 1);
            if (kb.IsKeyDown(Keys.Left) && _direction.X != 1) _direction = new Point(-1, 0);
            if (kb.IsKeyDown(Keys.Right) && _direction.X != -1) _direction = new Point(1, 0);
        }

        private void MoveSnake()
        {
            var head = _snake[0];
            var newHead = new Point(head.X + _direction.X, head.Y + _direction.Y);

            // wrap around using current grid size
            if (newHead.X < 0) newHead.X = _gridWidth - 1;
            if (newHead.X >= _gridWidth) newHead.X = 0;
            if (newHead.Y < 0) newHead.Y = _gridHeight - 1;
            if (newHead.Y >= _gridHeight) newHead.Y = 0;

            // Self-collision -> start death sequence (freeze frame, zoom, wait for restart)
            for (int i = 0; i < _snake.Count; i++)
            {
                if (_snake[i] == newHead)
                {
                    // start death instead of immediate reset
                    StartDeath();
                    return;
                }
            }

            _snake.Insert(0, newHead);
            if (!_growNextMove)
            {
                _snake.RemoveAt(_snake.Count - 1);
            }
            else
            {
                _growNextMove = false;
            }

            // cherry collision
            if (newHead == _cherry)
            {
                // streak logic: if we picked recently, grow streak; otherwise start new streak
                if (_timeSinceLastCherry <= StreakTimeout && _streak > 0)
                {
                    _streak = Math.Min(_streak + 1, MaxStreak);
                }
                else
                {
                    // new streak start: record the baseline burst count for this first cherry
                    _streak = 1;
                    _firstCherryBurstCount = 48 + Math.Min(200, _score * 8);
                }

                _timeSinceLastCherry = 0f;

                _score += 1;
                _growNextMove = true;
                PlaceCherry();

                // spawn burst sized by streak (cap multiplier to keep playable)
                int burstCount = Math.Min(_firstCherryBurstCount * Math.Max(1, _streak), _firstCherryBurstCount * MaxBurstMultiplier);
                SpawnBurstAroundSnake(newHead, burstCount);

                // smaller explosion burst too
                SpawnCherryBurst(new Vector2(newHead.X * CellSize + CellSize / 2f, newHead.Y * CellSize + CellSize / 2f));

                // expand window slightly each time a cherry is picked
                ExpandWindow();

                // trigger a short-lived emitter around the snake
                _cherryParticleTimer = CherryParticleDuration;

                // screenshake when streak >= 2
                if (_streak >= 2)
                {
                    _shakeTimer = _shakeDuration;
                    // amplitude scales with streak but has a max
                    _shakeAmplitude = Math.Min(MaxShakeAmplitude, 2f + _streak * 2.5f);
                }

                // Start the big streak display animation for the current streak
                _streakDisplayNumber = _streak;
                _streakPopTimer = StreakPopDuration;
                // cancel any fade-out in progress
                _streakFadeTimer = 0f;
                // reset idle anim time and pick a fresh phase so motion varies
                _streakIdleAnimTime = 0f;
                _streakPhase = (float)(_rng.NextDouble() * Math.PI * 2.0);
             }
        }

        private void StartDeath()
        {
            _deaths++;
            _isDead = true;
            _takeDeathSnapshot = true;
            _deathZoomTimer = 0f;
            _deathPopTimer = DeathPopDuration;
            _deathIdleAnimTime = 0f;
            _deathPhase = (float)(_rng.NextDouble() * Math.PI * 2.0);
            _deathDismissTimer = 0f;
            // leave current game state as-is; snapshot will be taken on next Draw
        }

        private void PlaceCherry()
        {
            // choose a random free cell
            var free = new List<Point>();
            for (int x = 0; x < _gridWidth; x++)
            for (int y = 0; y < _gridHeight; y++)
            {
                var p = new Point(x, y);
                if (!_snake.Contains(p)) free.Add(p);
            }

            if (free.Count == 0)
            {
                // full grid (win) -> reset
                ResetGame();
                return;
            }

            _cherry = free[_rng.Next(free.Count)];
        }

        private void SpawnBurstAroundSnake(Point gridPos, int count)
        {
            var center = new Vector2(gridPos.X * CellSize + CellSize / 2f, gridPos.Y * CellSize + CellSize / 2f);
            for (int i = 0; i < count && (_bgParticles.Count + _fgParticles.Count) < MaxParticles; i++)
            {
                var angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                var dist = (float)(_rng.NextDouble() * CellSize * 1.2);
                var pos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * dist;
                var speed = (float)(_rng.NextDouble() * 220 + 40) * (1f + 0.06f * _streak);
                var vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed + new Vector2((float)(_rng.NextDouble() * 40 - 20), (float)(_rng.NextDouble() * 40 - 20));
                var life = 0.8f + (float)(_rng.NextDouble() * 1.2f);
                var size = (float)(_rng.NextDouble() * 8 + 3) * (1f + _streak * 0.08f);
                // mark these as persistent (they stay around the snake and should be semi-transparent)
                SpawnParticle(pos, vel, life, _snakeOutline, size, background: false, persistent: true);
            }
        }

        // Emit a short-lived green trail at the snake's tail while speed boost is active
        private void EmitTailTrail(float dt)
        {
            if (_snake.Count == 0) return;

            // Determine tail position and orientation (last segment)
            var lastIndex = _snake.Count - 1;
            var tail = _snake[lastIndex];

            Vector2 tailDir;
            if (_snake.Count >= 2)
            {
                var prev = _snake[lastIndex - 1];
                tailDir = new Vector2(tail.X - prev.X, tail.Y - prev.Y);
            }
            else
            {
                // fallback: use inverse of current movement direction
                tailDir = new Vector2(-_direction.X, -_direction.Y);
            }

            if (tailDir == Vector2.Zero) tailDir = new Vector2(-1, 0);
            tailDir = Vector2.Normalize(tailDir);

            // screen-space center of the tail cell
            var tailCenter = new Vector2(tail.X * CellSize + CellSize / 2f, tail.Y * CellSize + CellSize / 2f);

            // spawn a few small green particles per frame; scale with streak but keep low per-frame
            int perFrame = Math.Min(6, 1 + _streak);
            for (int i = 0; i < perFrame && (_bgParticles.Count + _fgParticles.Count) < MaxParticles; i++)
            {
                // spawn slightly behind the tail along the tail direction
                var offsetDist = (float)(_rng.NextDouble() * CellSize * 0.6 + CellSize * 0.2);
                var pos = tailCenter + tailDir * -offsetDist; // behind tail

                // velocity primarily trailing away from tail (opposite of tail direction) with jitter
                var baseSpeed = (float)(_rng.NextDouble() * 40 + 20);
                var vel = tailDir * -baseSpeed + new Vector2((float)(_rng.NextDouble() * 40 - 20), (float)(_rng.NextDouble() * 40 - 20));

                var life = 0.35f + (float)(_rng.NextDouble() * 0.6f);
                var size = (float)(_rng.NextDouble() * 3f + 2f);

                SpawnParticle(pos, vel, life, _tailTrailColor, size, background: false, persistent: false);
            }
        }

        private void EmitAroundSnake(float dt)
        {
            // emit a modest number of particles per frame around head while timer active
            int perFrame = (8 + (int)(_score / 2)) * Math.Max(1, _streak);
            perFrame = Math.Min(perFrame, 60); // cap per-frame for performance
            var head = _snake[0];
            var center = new Vector2(head.X * CellSize + CellSize / 2f, head.Y * CellSize + CellSize / 2f);
            for (int i = 0; i < perFrame && (_bgParticles.Count + _fgParticles.Count) < MaxParticles; i++)
            {
                var angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                var dist = (float)(_rng.NextDouble() * CellSize * 0.9);
                var pos = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * dist;
                var vel = new Vector2((float)(_rng.NextDouble() * 80 - 40), (float)(_rng.NextDouble() * -80));
                var life = 0.6f + (float)(_rng.NextDouble() * 1.0f);
                var size = (float)(_rng.NextDouble() * 6 + 2) * (1f + _streak * 0.06f);
                // these are also persistent around-snake particles
                SpawnParticle(pos, vel, life, _snakeOutline, size, background: false, persistent: true);
            }
        }

        private void SpawnCherryBurst(Vector2 position)
        {
            // smaller additional pickup burst (foreground)
            int n = 36 + Math.Min(300, _score * 12);
            for (int i = 0; i < n && (_bgParticles.Count + _fgParticles.Count) < MaxParticles; i++)
            {
                var angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                var speed = (float)(_rng.NextDouble() * 220 + 60);
                var vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                var life = 0.6f + (float)(_rng.NextDouble() * 1.2f);
                var size = (float)(_rng.NextDouble() * 8 + 3) * (1f + _score * 0.08f);
                // explosion particles are not persistent and should be full opacity
                SpawnParticle(position, vel, life, _snakeOutline, size, background: false, persistent: false);
            }
        }

        // Expand the game window by half the cell size in both dimensions
        private void ExpandWindow()
        {
            var add = CellSize / 2; // half a segment
            // Increase preferred back buffer size and apply changes
            _graphics.PreferredBackBufferWidth += add;
            _graphics.PreferredBackBufferHeight += add;
            _graphics.ApplyChanges();

            // recompute grid size to match new window (cells are fixed size)
            _gridWidth = Math.Max(1, _graphics.PreferredBackBufferWidth / CellSize);
            _gridHeight = Math.Max(1, _graphics.PreferredBackBufferHeight / CellSize);
        }

        private void SpawnParticle(Vector2 pos, Vector2 vel, float life, Color color, float size, bool background = false, bool persistent = false)
        {
            if ((_bgParticles.Count + _fgParticles.Count) >= MaxParticles) return;
            var p = new Particle { Position = pos, Velocity = vel, Life = life, MaxLife = life, Color = color, Size = size, IsPersistent = persistent };
            if (background) _bgParticles.Add(p); else _fgParticles.Add(p);
        }

        private void UpdateParticleList(List<Particle> list, float dt)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                p.Velocity *= 0.98f; // drag
                p.Velocity += new Vector2(0, 40f) * dt; // gravity subtle
                p.Position += p.Velocity * dt;
                p.Life -= dt;
                list[i] = p;
                if (p.Life <= 0) list.RemoveAt(i);
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            // If we need to take a death snapshot, render the current scene into a RenderTarget first
            if (_takeDeathSnapshot)
            {
                // create snapshot RT matching backbuffer
                _deathSnapshot = new RenderTarget2D(GraphicsDevice, _graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);
                GraphicsDevice.SetRenderTarget(_deathSnapshot);
                // draw the full scene as-is onto the render target (no shake)
                DrawGameScene(includeShake: false);
                GraphicsDevice.SetRenderTarget(null);
                _takeDeathSnapshot = false;
            }

            if (_isDead && _deathSnapshot != null)
            {
                // draw zoomed snapshot
                GraphicsDevice.Clear(Color.Black);
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

                float t = MathHelper.Clamp(_deathZoomTimer / DeathZoomDuration, 0f, 1f);
                float zoom = 1f + (DeathZoomAmount - 1f) * (t);
                // center scale
                var center = new Vector2(_graphics.PreferredBackBufferWidth / 2f, _graphics.PreferredBackBufferHeight / 2f);
                var destRect = new Rectangle(0, 0, _graphics.PreferredBackBufferWidth, _graphics.PreferredBackBufferHeight);

                // When zooming we scale around center; draw snapshot scaled and offset
                var scaledW = _deathSnapshot.Width * zoom;
                var scaledH = _deathSnapshot.Height * zoom;
                var dst = new Rectangle((int)(center.X - scaledW / 2f), (int)(center.Y - scaledH / 2f), (int)scaledW, (int)scaledH);
                _spriteBatch.Draw(_deathSnapshot, dst, Color.White);

                // draw "You died" text with pop/idle, and instruction text
                DrawDeathUI();

                _spriteBatch.End();
            }
            else
            {
                // normal scene
                DrawGameScene(includeShake: true);
            }

            base.Draw(gameTime);
        }

        // Draw the full game scene (used for snapshotting and normal draw)
        private void DrawGameScene(bool includeShake)
        {
            // compute shake offset
            Vector2 shakeOffset = Vector2.Zero;
            if (includeShake && _shakeTimer > 0f)
            {
                float amt = _shakeAmplitude * (_shakeTimer / _shakeDuration);
                shakeOffset = new Vector2((float)((_rng.NextDouble() * 2.0) - 1.0), (float)((_rng.NextDouble() * 2.0) - 1.0)) * amt;
            }

            GraphicsDevice.Clear(new Color(20, 20, 25)); // dark background
            _spriteBatch.Begin(transformMatrix: Matrix.CreateTranslation(shakeOffset.X, shakeOffset.Y, 0), samplerState: SamplerState.PointClamp);

            // Draw background particles first
            for (int i = 0; i < _bgParticles.Count; i++)
            {
                var p = _bgParticles[i];
                var t = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                var col = p.Color * (0.4f * t); // make background more subtle
                var size = Math.Max(2f, p.Size * (0.5f + t * 0.7f));
                var rect = new Rectangle((int)(p.Position.X - size / 2), (int)(p.Position.Y - size / 2), (int)size, (int)size);
                _spriteBatch.Draw(_pixel, rect, col);
            }

            // Draw big popping streak number centered in the upper area (above background particles)
            if (_streakDisplayNumber > 0)
            {
                // overall visibility alpha from fade timer (if fading)
                float fadeAlpha = (_streakFadeTimer > 0f) ? (_streakFadeTimer / StreakFadeDuration) : 1f;

                // pop progress 0..1
                float popProgress = 1f;
                if (_streakPopTimer > 0f) popProgress = 1f - (_streakPopTimer / StreakPopDuration);
                popProgress = MathHelper.Clamp(popProgress, 0f, 1f);

                int num = _streakDisplayNumber;

                // scales: final settled scale and a larger peak for the pop
                float finalScaleF = 14f + num * 8f; // base settled size
                float peakScaleF = finalScaleF * 2.2f; // how big it pops

                // use a single-peaked sine curve so it grows to peak then settles back
                float sinPeak = MathF.Sin(popProgress * MathF.PI); // 0->1->0
                float curScaleF = finalScaleF + (peakScaleF - finalScaleF) * sinPeak;
                int curScale = Math.Max(6, (int)curScaleF);

                // alpha: strong during pop, slightly lower when settled, and multiplied by fadeAlpha
                float baseAlpha = (_streakPopTimer > 0f) ? 1f : 0.75f;
                float alpha = baseAlpha * fadeAlpha;
                var col = new Color(220, 40, 40) * alpha;

                // center position
                float centerX = _graphics.PreferredBackBufferWidth * 0.5f;
                float topY = MathF.Max(10f, _graphics.PreferredBackBufferHeight * 0.12f);
                // slight bob while popping
                float bob = 0f;
                // while popping, keep a stronger bob; otherwise use subtle idle floating motion
                if (_streakPopTimer > 0f)
                {
                    bob = MathF.Sin(popProgress * MathF.PI) * 12f;
                }
                else
                {
                    // idle float using sine/cos with phase for smoother motion
                    float fx = MathF.Cos(_streakIdleAnimTime + _streakPhase) * StreakIdleAmplitude * 0.35f;
                    float fy = MathF.Sin(_streakIdleAnimTime * 1.3f + _streakPhase * 0.7f) * StreakIdleAmplitude * 0.45f;
                    bob = fy;
                    // shift centerX slightly for horizontal float
                    centerX += fx * 0.6f;
                }
                var drawPos = new Vector2(centerX - (4 * curScale) * 0.5f, topY - (curScale * 2f) + bob);

                DrawPixelText(num.ToString(), drawPos, col, scale: curScale);
            }

            // Draw cherry (red square)
            DrawCell(_cherry, _cherryColor);

            // Draw snake segments (outline then fill)
            for (int i = _snake.Count - 1; i >= 0; i--)
            {
                var p = _snake[i];
                var screenPos = new Vector2(p.X * CellSize, p.Y * CellSize);

                // outline: slightly larger pink rectangle
                var outlineRect = new Rectangle((int)screenPos.X - 2, (int)screenPos.Y - 2, CellSize + 4, CellSize + 4);
                _spriteBatch.Draw(_pixel, outlineRect, _snakeOutline);

                // fill white
                var fillRect = new Rectangle((int)screenPos.X, (int)screenPos.Y, CellSize, CellSize);
                _spriteBatch.Draw(_pixel, fillRect, _snakeFill);

                // head face on the first (index 0)
                if (i == 0)
                {
                    var face = _faces[_currentFaceIndex];
                    // center the face inside the cell
                    DrawPixelText(face, screenPos + new Vector2(6, 8), Color.Black, scale: 2);
                }
            }

            // Draw score label "SCORE" using the same pixel font and keep numeric score to the right
            DrawPixelText("SCORE", _scorePosition, _scoreColor, scale: 2);
            // move score number farther right for readability
            DrawPixelText(_score.ToString(), _scorePosition + new Vector2(68, 0), _scoreColor, scale: 2);
            // Draw deaths label further down and move its number farther right
            var deathsLabelPos = _scorePosition + new Vector2(0, 28);
            DrawPixelText("DEATHS", deathsLabelPos, _scoreColor, scale: 2);
            DrawPixelText(_deaths.ToString(), deathsLabelPos + new Vector2(68, 0), _scoreColor, scale: 2);

            // Draw foreground particles (bursts around snake)
            for (int i = 0; i < _fgParticles.Count; i++)
            {
                var p = _fgParticles[i];
                var t = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                // persistent around-snake particles should be 75% transparent (25% opaque)
                float baseAlpha = p.IsPersistent ? 0.25f : 1f;
                var col = p.Color * (baseAlpha * t);
                // render particles larger on screen; keep minimum size
                var size = Math.Max(2f, p.Size * (0.6f + t * 0.8f));
                var rect = new Rectangle((int)(p.Position.X - size / 2), (int)(p.Position.Y - size / 2), (int)size, (int)size);
                _spriteBatch.Draw(_pixel, rect, col);
            }

            _spriteBatch.End();
        }

        private void DrawDeathUI()
        {
            // draw "You died" centered, with pop/idle and dismissal swoosh
            float popProgress = 1f;
            if (_deathPopTimer > 0f) popProgress = 1f - (_deathPopTimer / DeathPopDuration);
            popProgress = MathHelper.Clamp(popProgress, 0f, 1f);

            float sinPeak = MathF.Sin(popProgress * MathF.PI);
            float baseScale = 18f;
            float peakScale = baseScale * 2.0f;
            float curScaleF = baseScale + (peakScale - baseScale) * sinPeak;
            int curScale = Math.Max(8, (int)curScaleF);

            float bob = 0f;
            if (_deathPopTimer > 0f) bob = MathF.Sin(popProgress * MathF.PI) * 14f; else bob = MathF.Sin(_deathIdleAnimTime * 1.1f + _deathPhase) * 10f;

            // dismissal swoosh progress
            float dismiss = 0f;
            if (_deathDismissTimer > 0f) dismiss = 1f - (_deathDismissTimer / DeathDismissDuration);
            dismiss = MathHelper.Clamp(dismiss, 0f, 1f);

            var center = new Vector2(_graphics.PreferredBackBufferWidth * 0.5f, _graphics.PreferredBackBufferHeight * 0.45f);
            // compute measured widths so we can center exactly
            float mainWidth = MeasurePixelTextWidth("YOU DIED", curScale);
            var pos = center + new Vector2(dismiss * 600f, - (curScale * 2f) + bob) - new Vector2(mainWidth * 0.5f, 0);

            // color and alpha reduce on dismiss
            float alpha = 1f - dismiss;
            var col = new Color(220, 40, 40) * alpha;

            DrawPixelText("YOU DIED", pos, col, scale: curScale);

            // instruction text below (use slightly larger scale for readability)
            int instrScale = 3;
            float instrWidth = MeasurePixelTextWidth("PRESS SPACE TO PLAY AGAIN", instrScale);
            var instrPos = center + new Vector2(0, 80f + bob) - new Vector2(instrWidth * 0.5f, 0);
            DrawPixelText("PRESS SPACE TO PLAY AGAIN", instrPos, new Color(200, 200, 200) * alpha, scale: instrScale);
        }

        private float MeasurePixelTextWidth(string text, int scale)
        {
            // each glyph is 3 pixels wide + 1 pixel spacing = 4 columns
            return text.Length * 4f * scale;
        }

        // Draws a 3x5 pixel font for digits + basic symbols.
        private readonly Dictionary<char, byte[]> _fontMap = new()
        {
            // Each byte is a row, 3 bits used (lsb = left)
            ['0'] = new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
            ['1'] = new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 },
            ['2'] = new byte[] { 0b111, 0b001, 0b111, 0b100, 0b111 },
            ['3'] = new byte[] { 0b111, 0b001, 0b111, 0b001, 0b111 },
            ['4'] = new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 },
            ['5'] = new byte[] { 0b111, 0b100, 0b111, 0b001, 0b111 },
            ['6'] = new byte[] { 0b111, 0b100, 0b111, 0b101, 0b111 },
            ['7'] = new byte[] { 0b111, 0b001, 0b010, 0b010, 0b010 },
            ['8'] = new byte[] { 0b111, 0b101, 0b111, 0b101, 0b111 },
            ['9'] = new byte[] { 0b111, 0b101, 0b111, 0b001, 0b111 },
            [':'] = new byte[] { 0b000, 0b010, 0b000, 0b010, 0b000 },

            // Uppercase letters used in the score label
            ['S'] = new byte[] { 0b111, 0b100, 0b111, 0b001, 0b111 },
            ['C'] = new byte[] { 0b111, 0b100, 0b100, 0b100, 0b111 },
            ['O'] = new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
            ['R'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b101 },
            ['E'] = new byte[] { 0b111, 0b100, 0b111, 0b100, 0b111 },

            // Extra glyphs for cute faces
            ['^'] = new byte[] { 0b010, 0b101, 0b000, 0b000, 0b000 },
            ['w'] = new byte[] { 0b101, 0b101, 0b101, 0b111, 0b000 },
            [' '] = new byte[] { 0b000, 0b000, 0b000, 0b000, 0b000 },
            ['U'] = new byte[] { 0b101, 0b101, 0b101, 0b101, 0b111 },
            // small wedge characters (˃ and ˂) map to similar angled shapes
            ['˃'] = new byte[] { 0b001, 0b010, 0b100, 0b010, 0b001 },
            ['˂'] = new byte[] { 0b100, 0b010, 0b001, 0b010, 0b100 },
            ['('] = new byte[] { 0b010, 0b100, 0b100, 0b100, 0b010 },
            [')'] = new byte[] { 0b010, 0b001, 0b001, 0b001, 0b010 },
            ['n'] = new byte[] { 0b000, 0b110, 0b101, 0b101, 0b101 },
            ['_'] = new byte[] { 0b000, 0b000, 0b000, 0b000, 0b111 },

            // Deaths label letters
            ['D'] = new byte[] { 0b110, 0b101, 0b101, 0b101, 0b110 }, // clearer D
            ['A'] = new byte[] { 0b010, 0b101, 0b111, 0b101, 0b101 }, // standard A
            ['T'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b010 },
            ['H'] = new byte[] { 0b101, 0b101, 0b111, 0b101, 0b101 },
            // Additional uppercase glyphs used by death screen and instructions
            ['Y'] = new byte[] { 0b101, 0b010, 0b010, 0b010, 0b010 },
            ['I'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b111 },
            ['P'] = new byte[] { 0b110, 0b101, 0b110, 0b100, 0b100 },
            ['L'] = new byte[] { 0b100, 0b100, 0b100, 0b100, 0b111 },
            ['G'] = new byte[] { 0b111, 0b100, 0b101, 0b101, 0b111 },
            ['N'] = new byte[] { 0b101, 0b111, 0b111, 0b101, 0b101 },
        };

        // Draw pixel text using the small font above
        private void DrawPixelText(string text, Vector2 pos, Color color, int scale = 2)
        {
            var x = pos.X;
            foreach (var ch in text)
            {
                if (!_fontMap.TryGetValue(ch, out var pattern))
                {
                    // skip unknown chars (avoid fallback showing 'S')
                    x += 4 * scale; // still advance for spacing so faces keep relative layout
                    continue;
                }

                for (int row = 0; row < pattern.Length; row++)
                {
                    var rowBits = pattern[row];
                    for (int col = 0; col < 3; col++)
                    {
                        if (((rowBits >> (2 - col)) & 1) != 0)
                        {
                            var px = (int)(x + col * scale);
                            var py = (int)(pos.Y + row * scale);
                            var rect = new Rectangle(px, py, scale, scale);
                            _spriteBatch.Draw(_pixel, rect, color);
                        }
                    }
                }

                x += 4 * scale; // advance (3 cols + 1 spacing)
            }
        }

        // Draw a grid cell at given grid coordinates
        private void DrawCell(Point cell, Color color)
        {
            var rect = new Rectangle(cell.X * CellSize, cell.Y * CellSize, CellSize, CellSize);
            _spriteBatch.Draw(_pixel, rect, color);
        }

        private struct Particle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Life;
            public float MaxLife;
            public Color Color;
            public float Size;
            public bool IsPersistent; // true for particles that stay around snake (semi-transparent)
        }
    }
}
