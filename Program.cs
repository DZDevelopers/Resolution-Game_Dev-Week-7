using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

public class Program
{
    const int Width = 960;
    const int Height = 540;

    const float PlayerRadius = 16f;
    const float PlayerBaseSpeed = 220f;
    const int PlayerBaseMaxHealth = 5;

    const float BulletRadius = 5f;
    const float BulletSpeed = 560f;
    const float BaseFireCooldown = 0.28f;
    const int BaseBulletDamage = 1;

    const float EnemyRadius = 15f;
    const float EnemyBaseSpeed = 70f;
    const int EnemyBaseHealth = 2;
    const float EnemyContactDamageCooldown = 0.6f;

    const float TurretOrbitRadius = 55f;
    const float TurretOrbitSpeed = 2.2f;
    const float TurretFireCooldown = 0.7f;
    const float TurretRange = 260f;

    static Random rng = new Random();

    static Vector2 player;
    static int playerHealth;
    static float fireTimer = 0;
    static float invincibleTimer = 0;

    class Enemy
    {
        public Vector2 Pos;
        public int Health;
        public float HitFlash;
    }
    static List<Enemy> enemies = new();

    class Bullet
    {
        public Vector2 Pos;
        public Vector2 Vel;
    }
    static List<Bullet> bullets = new();

    class Turret
    {
        public float OrbitAngle;
        public float FireTimer;
    }
    static List<Turret> turrets = new();

    enum GameState { Menu, Playing, Paused, WaveClear, Upgrade, GameOver }
    static GameState state = GameState.Menu;

    static int wave = 1;
    static float waveTransitionTimer = 0;
    const float WaveTransitionDelay = 1.2f;

    static float timeAlive = 0;
    static int score = 0;
    static int highScoreWave = 0;

    static float shakeTimer = 0;
    static float shakeStrength = 0;

    enum UpgradeType { FireRate, Damage, MoveSpeed, MaxHealth, AutoTurret }

    class UpgradeOption
    {
        public UpgradeType Type;
        public string Name;
        public string Description;
    }

    static readonly Dictionary<UpgradeType, (string Name, string Desc)> UpgradeInfo = new()
    {
        { UpgradeType.FireRate,   ("Fire Rate",   "Shoot faster") },
        { UpgradeType.Damage,     ("Damage",      "Bullets hit harder") },
        { UpgradeType.MoveSpeed,  ("Move Speed",  "Move faster") },
        { UpgradeType.MaxHealth,  ("Max Health",  "+1 max HP, heals you now") },
        { UpgradeType.AutoTurret, ("Auto-Turret", "Orbiting drone that fires at the nearest enemy on its own") },
    };

    static Dictionary<UpgradeType, int> upgradeStacks = new()
    {
        { UpgradeType.FireRate, 0 },
        { UpgradeType.Damage, 0 },
        { UpgradeType.MoveSpeed, 0 },
        { UpgradeType.MaxHealth, 0 },
        { UpgradeType.AutoTurret, 0 },
    };

    static List<UpgradeOption> currentChoices = new();
    static int selectedUpgradeIndex = 0;

    const float FireRatePerStack = 0.035f;
    const float MinFireCooldown = 0.07f;
    const int DamagePerStack = 1;
    const float MoveSpeedPerStack = 28f;
    const int MaxHealthPerStack = 1;

    static float CurrentFireCooldown =>
        MathF.Max(MinFireCooldown, BaseFireCooldown - upgradeStacks[UpgradeType.FireRate] * FireRatePerStack);

    static int CurrentBulletDamage =>
        BaseBulletDamage + upgradeStacks[UpgradeType.Damage] * DamagePerStack;

    static float CurrentPlayerSpeed =>
        PlayerBaseSpeed + upgradeStacks[UpgradeType.MoveSpeed] * MoveSpeedPerStack;

    static int CurrentMaxHealth =>
        PlayerBaseMaxHealth + upgradeStacks[UpgradeType.MaxHealth] * MaxHealthPerStack;

    public static void Main()
    {
        Raylib.InitWindow(Width, Height, "Wave Shooter");
        Raylib.SetTargetFPS(60);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            switch (state)
            {
                case GameState.Menu:
                    UpdateMenu();
                    break;

                case GameState.Playing:
                    UpdatePlayer(dt);
                    UpdateShooting(dt);
                    UpdateBullets(dt);
                    UpdateTurrets(dt);
                    UpdateEnemies(dt);
                    CheckCollisions();
                    timeAlive += dt;

                    if (invincibleTimer > 0)
                        invincibleTimer -= dt;

                    if (enemies.Count == 0)
                    {
                        waveTransitionTimer += dt;
                        if (waveTransitionTimer >= WaveTransitionDelay)
                            OpenUpgradeChoice();
                    }

                    if (Raylib.IsKeyPressed(KeyboardKey.P) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                        state = GameState.Paused;
                    break;

                case GameState.Paused:
                    if (Raylib.IsKeyPressed(KeyboardKey.P) || Raylib.IsKeyPressed(KeyboardKey.Escape))
                        state = GameState.Playing;
                    break;

                case GameState.Upgrade:
                    UpdateUpgradeChoice();
                    break;

                case GameState.GameOver:
                    if (Raylib.IsKeyPressed(KeyboardKey.R))
                        Reset();
                    if (Raylib.IsKeyPressed(KeyboardKey.M))
                        state = GameState.Menu;
                    break;
            }

            if (shakeTimer > 0)
                shakeTimer -= dt;

            Draw();
        }

        Raylib.CloseWindow();
    }

    static void UpdateMenu()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            Reset();
        if (Raylib.IsKeyPressed(KeyboardKey.Q))
            Raylib.CloseWindow();
    }

    static void Reset()
    {
        player = new Vector2(Width / 2, Height / 2);
        playerHealth = PlayerBaseMaxHealth;
        fireTimer = 0;
        invincibleTimer = 0;

        bullets.Clear();
        turrets.Clear();
        enemies.Clear();

        wave = 1;
        waveTransitionTimer = 0;
        timeAlive = 0;
        score = 0;
        shakeTimer = 0;

        foreach (var key in new List<UpgradeType>(upgradeStacks.Keys))
            upgradeStacks[key] = 0;

        SpawnWave();
        state = GameState.Playing;
    }

    static void SpawnWave()
    {
        enemies.Clear();
        waveTransitionTimer = 0;

        int count = 4 + wave * 2;
        for (int i = 0; i < count; i++)
        {
            enemies.Add(new Enemy
            {
                Pos = RandomEdge(),
                Health = EnemyBaseHealth + wave / 2,
                HitFlash = 0
            });
        }
    }

    static void UpdatePlayer(float dt)
    {
        Vector2 dir = Vector2.Zero;

        if (Raylib.IsKeyDown(KeyboardKey.W)) dir.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) dir.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) dir.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) dir.X += 1;

        if (dir != Vector2.Zero)
            dir = Vector2.Normalize(dir);

        player += dir * CurrentPlayerSpeed * dt;
        player.X = Clamp(player.X, PlayerRadius, Width - PlayerRadius);
        player.Y = Clamp(player.Y, PlayerRadius, Height - PlayerRadius);
    }

    static void UpdateShooting(float dt)
    {
        if (fireTimer > 0)
            fireTimer -= dt;

        if (Raylib.IsMouseButtonDown(MouseButton.Left) && fireTimer <= 0)
        {
            Vector2 mouse = Raylib.GetMousePosition();
            Vector2 dir = mouse - player;
            dir = dir != Vector2.Zero ? Vector2.Normalize(dir) : new Vector2(1, 0);

            bullets.Add(new Bullet { Pos = player, Vel = dir * BulletSpeed });
            fireTimer = CurrentFireCooldown;
        }
    }

    static void UpdateBullets(float dt)
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            b.Pos += b.Vel * dt;
            bullets[i] = b;

            if (b.Pos.X < -20 || b.Pos.X > Width + 20 || b.Pos.Y < -20 || b.Pos.Y > Height + 20)
                bullets.RemoveAt(i);
        }
    }

    static void UpdateTurrets(float dt)
    {
        foreach (var t in turrets)
        {
            t.OrbitAngle += TurretOrbitSpeed * dt;
            if (t.FireTimer > 0)
                t.FireTimer -= dt;

            Vector2 turretPos = player + new Vector2(MathF.Cos(t.OrbitAngle), MathF.Sin(t.OrbitAngle)) * TurretOrbitRadius;

            if (t.FireTimer <= 0)
            {
                var target = FindNearestEnemy(turretPos, TurretRange);
                if (target != null)
                {
                    var dir = Vector2.Normalize(target.Pos - turretPos);
                    bullets.Add(new Bullet { Pos = turretPos, Vel = dir * BulletSpeed });
                    t.FireTimer = TurretFireCooldown;
                }
            }
        }
    }

    static Enemy FindNearestEnemy(Vector2 from, float maxRange)
    {
        Enemy best = null;
        float bestDist = maxRange;

        foreach (var e in enemies)
        {
            float d = Vector2.Distance(from, e.Pos);
            if (d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }

        return best;
    }

    static void UpdateEnemies(float dt)
    {
        float speedMul = 1f + (wave - 1) * 0.08f;

        foreach (var e in enemies)
        {
            Vector2 toPlayer = player - e.Pos;
            if (toPlayer != Vector2.Zero)
                toPlayer = Vector2.Normalize(toPlayer);

            e.Pos += toPlayer * EnemyBaseSpeed * speedMul * dt;

            if (e.HitFlash > 0)
                e.HitFlash -= dt;
        }
    }

    static void CheckCollisions()
    {
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            bool hit = false;

            for (int j = enemies.Count - 1; j >= 0; j--)
            {
                var e = enemies[j];
                if (Vector2.Distance(b.Pos, e.Pos) < BulletRadius + EnemyRadius)
                {
                    e.Health -= CurrentBulletDamage;
                    e.HitFlash = 0.1f;
                    hit = true;

                    if (e.Health <= 0)
                    {
                        enemies.RemoveAt(j);
                        score += 10;
                    }
                    break;
                }
            }

            if (hit)
                bullets.RemoveAt(i);
        }

        if (invincibleTimer <= 0)
        {
            foreach (var e in enemies)
            {
                if (Vector2.Distance(player, e.Pos) < PlayerRadius + EnemyRadius)
                {
                    TakeDamage();
                    break;
                }
            }
        }
    }

    static void TakeDamage()
    {
        playerHealth--;
        invincibleTimer = EnemyContactDamageCooldown;
        shakeTimer = 0.2f;
        shakeStrength = 5f;

        if (playerHealth <= 0)
            TriggerGameOver();
    }

    static void TriggerGameOver()
    {
        state = GameState.GameOver;
        shakeTimer = 0.35f;
        shakeStrength = 8f;

        if (wave > highScoreWave)
            highScoreWave = wave;
    }

    static void OpenUpgradeChoice()
    {
        var types = new List<UpgradeType>((UpgradeType[])Enum.GetValues(typeof(UpgradeType)));
        currentChoices.Clear();

        for (int i = 0; i < 3 && types.Count > 0; i++)
        {
            int idx = rng.Next(types.Count);
            var t = types[idx];
            types.RemoveAt(idx);

            currentChoices.Add(new UpgradeOption
            {
                Type = t,
                Name = UpgradeInfo[t].Name,
                Description = UpgradeInfo[t].Desc
            });
        }

        selectedUpgradeIndex = 0;
        state = GameState.Upgrade;
    }

    static void UpdateUpgradeChoice()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.Left))
            selectedUpgradeIndex = (selectedUpgradeIndex - 1 + currentChoices.Count) % currentChoices.Count;
        if (Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Right))
            selectedUpgradeIndex = (selectedUpgradeIndex + 1) % currentChoices.Count;

        if (Raylib.IsKeyPressed(KeyboardKey.One)) selectedUpgradeIndex = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Two) && currentChoices.Count > 1) selectedUpgradeIndex = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Three) && currentChoices.Count > 2) selectedUpgradeIndex = 2;

        bool numberPicked = Raylib.IsKeyPressed(KeyboardKey.One) ||
            (Raylib.IsKeyPressed(KeyboardKey.Two) && currentChoices.Count > 1) ||
            (Raylib.IsKeyPressed(KeyboardKey.Three) && currentChoices.Count > 2);

        if (numberPicked || Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            ApplyUpgrade(currentChoices[selectedUpgradeIndex].Type);
            wave++;
            SpawnWave();
            state = GameState.Playing;
        }
    }

    static void ApplyUpgrade(UpgradeType type)
    {
        upgradeStacks[type]++;

        if (type == UpgradeType.MaxHealth)
        {
            playerHealth = CurrentMaxHealth;
        }
        else if (type == UpgradeType.AutoTurret)
        {
            turrets.Add(new Turret
            {
                OrbitAngle = turrets.Count * (MathF.PI * 2f / 3f),
                FireTimer = 0
            });
        }
    }

    static void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(18, 18, 26, 255));

        Vector2 shakeOffset = Vector2.Zero;
        if (shakeTimer > 0)
        {
            shakeOffset = new Vector2(
                (float)(rng.NextDouble() * 2 - 1) * shakeStrength,
                (float)(rng.NextDouble() * 2 - 1) * shakeStrength
            );
        }

        if (state == GameState.Menu)
            DrawMenu();
        else
            DrawGame(shakeOffset);

        Raylib.EndDrawing();
    }

    static void DrawMenu()
    {
        string title = "WAVE SHOOTER";
        int titleSize = 48;
        int titleWidth = Raylib.MeasureText(title, titleSize);
        Raylib.DrawText(title, Width / 2 - titleWidth / 2, 120, titleSize, Color.White);

        DrawCentered("Press ENTER or SPACE to Play", 230, 22, Color.SkyBlue);
        DrawCentered("Press Q to Quit", 265, 20, Color.Gray);
        DrawCentered("WASD to move  |  Aim and hold Left Click to shoot", 350, 18, Color.LightGray);
        DrawCentered("Clear each wave to choose an upgrade", 378, 18, Color.LightGray);

        if (highScoreWave > 0)
            DrawCentered($"Best Wave Reached: {highScoreWave}", 420, 20, Color.Gold);
    }

    static void DrawGame(Vector2 shakeOffset)
    {
        Vector2 Off(Vector2 v) => v + shakeOffset;

        foreach (var t in turrets)
        {
            Vector2 tp = player + new Vector2(MathF.Cos(t.OrbitAngle), MathF.Sin(t.OrbitAngle)) * TurretOrbitRadius;
            Raylib.DrawCircleV(Off(tp), 7f, Color.Lime);
        }

        foreach (var e in enemies)
        {
            var c = e.HitFlash > 0 ? Color.White : Color.Red;
            Raylib.DrawCircleV(Off(e.Pos), EnemyRadius, c);
        }

        foreach (var b in bullets)
            Raylib.DrawCircleV(Off(b.Pos), BulletRadius, Color.Yellow);

        bool flashOn = invincibleTimer <= 0 || ((int)(invincibleTimer * 20) % 2 == 0);
        if (flashOn)
            Raylib.DrawCircleV(Off(player), PlayerRadius, Color.Blue);

        if (state == GameState.Playing)
        {
            Vector2 mouse = Raylib.GetMousePosition();
            Vector2 dir = mouse - player;
            if (dir != Vector2.Zero)
            {
                dir = Vector2.Normalize(dir);
                Vector2 lineEnd = player + dir * 28f;
                Raylib.DrawLineEx(Off(player), Off(lineEnd), 2f, new Color(255, 255, 255, 150));
            }
        }

        DrawHud();

        if (state == GameState.Paused)
        {
            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 150));
            DrawCentered("PAUSED", 230, 40, Color.White);
            DrawCentered("Press P or ESC to resume", 280, 20, Color.LightGray);
        }

        if (state == GameState.Upgrade)
            DrawUpgradeChoice();

        if (state == GameState.GameOver)
        {
            Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 150));
            DrawCentered("GAME OVER", 220, 40, Color.Red);
            DrawCentered($"Reached Wave {wave}", 265, 22, Color.White);
            DrawCentered($"Score: {score}", 295, 20, Color.White);
            DrawCentered("Press R to restart  |  M for menu", 330, 20, Color.LightGray);
        }
    }

    static void DrawHud()
    {
        Raylib.DrawText($"Wave: {wave}", 20, 20, 22, Color.White);
        Raylib.DrawText($"Score: {score}", 20, 46, 18, Color.LightGray);

        for (int i = 0; i < CurrentMaxHealth; i++)
        {
            var c = i < playerHealth ? Color.Red : new Color(60, 60, 60, 255);
            Raylib.DrawCircle(20 + i * 22, 80, 7, c);
        }

        if (enemies.Count > 0)
            Raylib.DrawText($"Enemies left: {enemies.Count}", 20, 100, 16, Color.Gray);

        int x = Width - 20;
        foreach (var kv in upgradeStacks)
        {
            if (kv.Value <= 0) continue;
            string label = $"{ShortName(kv.Key)} x{kv.Value}";
            int w = Raylib.MeasureText(label, 16);
            x -= w + 14;
            Raylib.DrawText(label, x, 22, 16, Color.SkyBlue);
        }
    }

    static string ShortName(UpgradeType t) => t switch
    {
        UpgradeType.FireRate => "Rate",
        UpgradeType.Damage => "Dmg",
        UpgradeType.MoveSpeed => "Speed",
        UpgradeType.MaxHealth => "HP",
        UpgradeType.AutoTurret => "Turret",
        _ => t.ToString()
    };

    static void DrawUpgradeChoice()
    {
        Raylib.DrawRectangle(0, 0, Width, Height, new Color(0, 0, 0, 170));
        DrawCentered($"WAVE {wave} CLEAR!", 60, 32, Color.Gold);
        DrawCentered("Choose an upgrade", 100, 20, Color.LightGray);

        int cardCount = currentChoices.Count;
        int cardW = 220;
        int cardH = 220;
        int gap = 30;
        int totalW = cardCount * cardW + (cardCount - 1) * gap;
        int startX = Width / 2 - totalW / 2;
        int cardY = 150;

        for (int i = 0; i < cardCount; i++)
        {
            int cx = startX + i * (cardW + gap);
            bool selected = i == selectedUpgradeIndex;

            var border = selected ? Color.Gold : Color.Gray;
            var fill = selected ? new Color(50, 50, 70, 255) : new Color(35, 35, 45, 255);

            Raylib.DrawRectangle(cx, cardY, cardW, cardH, fill);
            Raylib.DrawRectangleLinesEx(new Rectangle(cx, cardY, cardW, cardH), selected ? 4 : 2, border);

            var opt = currentChoices[i];
            int stacks = upgradeStacks[opt.Type];

            Raylib.DrawText($"{i + 1}", cx + 16, cardY + 12, 22, Color.LightGray);

            int nameW = Raylib.MeasureText(opt.Name, 20);
            Raylib.DrawText(opt.Name, cx + cardW / 2 - nameW / 2, cardY + 50, 20, Color.White);
            DrawWrapped(opt.Description, cx + 16, cardY + 90, cardW - 32, 16, Color.LightGray);

            if (stacks > 0)
            {
                string stackText = $"Owned: {stacks}";
                int sw = Raylib.MeasureText(stackText, 16);
                Raylib.DrawText(stackText, cx + cardW / 2 - sw / 2, cardY + cardH - 30, 16, Color.SkyBlue);
            }
        }

        DrawCentered("A/D or Arrows to pick, Enter/Space to confirm (or press 1/2/3)", Height - 50, 16, Color.LightGray);
    }

    static void DrawWrapped(string text, int x, int y, int maxWidth, int fontSize, Color color)
    {
        string[] words = text.Split(' ');
        string line = "";
        int lineY = y;

        foreach (var word in words)
        {
            string test = line.Length == 0 ? word : line + " " + word;
            if (Raylib.MeasureText(test, fontSize) > maxWidth)
            {
                Raylib.DrawText(line, x, lineY, fontSize, color);
                line = word;
                lineY += fontSize + 4;
            }
            else
            {
                line = test;
            }
        }

        if (line.Length > 0)
            Raylib.DrawText(line, x, lineY, fontSize, color);
    }

    static void DrawCentered(string text, int y, int size, Color color)
    {
        int w = Raylib.MeasureText(text, size);
        Raylib.DrawText(text, Width / 2 - w / 2, y, size, color);
    }

    static Vector2 RandomEdge()
    {
        int side = rng.Next(4);
        return side switch
        {
            0 => new Vector2(rng.Next(Width), 0),
            1 => new Vector2(rng.Next(Width), Height),
            2 => new Vector2(0, rng.Next(Height)),
            _ => new Vector2(Width, rng.Next(Height))
        };
    }

    static float Clamp(float v, float min, float max)
        => MathF.Max(min, MathF.Min(max, v));
}