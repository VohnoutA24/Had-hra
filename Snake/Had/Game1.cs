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
        private const int GridWidth = 20;
        private const int GridHeight = 15;

        // Snake
        private readonly List<Point> _snake = new();
        private Point _direction = new(1, 0);
        private double _moveTimer;
        private const double MoveInterval = 0.12; // seconds
        private bool _growNextMove;

        // Apple
        private Point _apple;
        private readonly Random _rng = new();

        // Score
        private int _score;

        // Rendering helpers
        private Texture2D _pixel = null!;
        private readonly Color _snakeFill = Color.White;
        private readonly Color _snakeOutline = new Color(255, 128, 200); // pink
        private readonly Color _appleColor = Color.Red;
        private readonly Color _scoreColor = Color.White;
        private readonly Vector2 _scorePosition = new(8, 8);

        // Particles
        private readonly List<Particle> _particles = new();
        private const int MaxParticles = 800;

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

            // set window size based on grid
            _graphics.PreferredBackBufferWidth = GridWidth * CellSize;
            _graphics.PreferredBackBufferHeight = GridHeight * CellSize;
            _graphics.ApplyChanges();
        }

        protected override void Initialize()
        {
            // Initialize snake in center
            _snake.Clear();
            var start = new Point(GridWidth / 2, GridHeight / 2);
            _snake.Add(start);
            _snake.Add(new Point(start.X - 1, start.Y));
            _snake.Add(new Point(start.X - 2, start.Y));
            _direction = new Point(1, 0);
            _moveTimer = 0;
            _growNextMove = false;

            PlaceApple();

            base.Initialize();
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

            var dt = gameTime.ElapsedGameTime.TotalSeconds;
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

            UpdateParticles((float)dt);

            // Score emitter - spawn a small shivering pink particle near the score every frame (controlled)
            if (_particles.Count < MaxParticles)
            {
                var basePos = new Vector2(_scorePosition.X + 40, _scorePosition.Y + 8);
                SpawnParticle(basePos + new Vector2((float)(_rng.NextDouble() * 6 - 3), (float)(_rng.NextDouble() * 6 - 3)),
                    new Vector2((float)(_rng.NextDouble() * 20 - 10), (float)(_rng.NextDouble() * -20)), 0.6f,
                    _snakeOutline, (float)(_rng.NextDouble() * 2 + 1));
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

            // wrap around
            if (newHead.X < 0) newHead.X = GridWidth - 1;
            if (newHead.X >= GridWidth) newHead.X = 0;
            if (newHead.Y < 0) newHead.Y = GridHeight - 1;
            if (newHead.Y >= GridHeight) newHead.Y = 0;

            // Self-collision -> reset game (simple)
            for (int i = 0; i < _snake.Count; i++)
            {
                if (_snake[i] == newHead)
                {
                    // reset
                    Initialize();
                    _score = 0;
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

            // apple collision
            if (newHead == _apple)
            {
                _score += 1;
                _growNextMove = true;
                PlaceApple();
                SpawnAppleBurst(new Vector2(newHead.X * CellSize + CellSize / 2f, newHead.Y * CellSize + CellSize / 2f));
            }
        }

        private void PlaceApple()
        {
            // choose a random free cell
            var free = new List<Point>();
            for (int x = 0; x < GridWidth; x++)
            for (int y = 0; y < GridHeight; y++)
            {
                var p = new Point(x, y);
                if (!_snake.Contains(p)) free.Add(p);
            }

            if (free.Count == 0)
            {
                // full grid (win) -> reset
                Initialize();
                return;
            }

            _apple = free[_rng.Next(free.Count)];
        }

        private void SpawnAppleBurst(Vector2 position)
        {
            // burst of pink particles
            int n = 36;
            for (int i = 0; i < n; i++)
            {
                if (_particles.Count >= MaxParticles) break;
                var angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                var speed = (float)(_rng.NextDouble() * 120 + 40);
                var vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                var life = (float)(_rng.NextDouble() * 0.7 + 0.4);
                var size = (float)(_rng.NextDouble() * 3 + 2);
                SpawnParticle(position, vel, life, _snakeOutline, size);
            }
        }

        private void SpawnParticle(Vector2 pos, Vector2 vel, float life, Color color, float size)
        {
            _particles.Add(new Particle { Position = pos, Velocity = vel, Life = life, MaxLife = life, Color = color, Size = size });
        }

        private void UpdateParticles(float dt)
        {
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.Velocity *= 0.98f; // drag
                p.Velocity += new Vector2(0, 50f) * dt; // gravity subtle
                p.Position += p.Velocity * dt;
                p.Life -= dt;
                _particles[i] = p;
                if (p.Life <= 0) _particles.RemoveAt(i);
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(20, 20, 25)); // dark background

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Draw apple (red square)
            DrawCell(_apple, _appleColor);

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
            DrawPixelText(_score.ToString(), _scorePosition + new Vector2(44, 0), _scoreColor, scale: 2);

            // Draw particles (as circles approximated by square with alpha)
            for (int i = 0; i < _particles.Count; i++)
            {
                var p = _particles[i];
                var t = MathHelper.Clamp(p.Life / p.MaxLife, 0f, 1f);
                var col = p.Color * t;
                var size = Math.Max(1f, p.Size * (0.5f + t * 0.5f));
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
        }
    }
}
