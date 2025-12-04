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
        private readonly Color _scoreColor = Color.White;
        private readonly Vector2 _scorePosition = new(8, 8);

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
            _moveTimer += dt;
            if (_moveTimer >= MoveInterval)
            {
                _moveTimer -= MoveInterval;
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

            // Self-collision -> reset game (simple)
            for (int i = 0; i < _snake.Count; i++)
            {
                if (_snake[i] == newHead)
                {
                    // reset
                    _deaths++;
                    _score = 0;
                    ResetGame();
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
            }
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
            GraphicsDevice.Clear(new Color(20, 20, 25)); // dark background

            // compute shake offset
            Vector2 shakeOffset = Vector2.Zero;
            if (_shakeTimer > 0f)
            {
                float amt = _shakeAmplitude * (_shakeTimer / _shakeDuration);
                shakeOffset = new Vector2((float)((_rng.NextDouble() * 2.0) - 1.0), (float)((_rng.NextDouble() * 2.0) - 1.0)) * amt;
            }

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

            base.Draw(gameTime);
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
