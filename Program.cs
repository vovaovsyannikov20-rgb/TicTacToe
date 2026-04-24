using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace TicTacToe.Server
{
    class Program
    {
        private static TcpListener _listener;
        private static TcpClient _client1;
        private static TcpClient _client2;
        private static char[] _board = new char[9];
        private static int _currentPlayer = 0; // 0 = X, 1 = O
        private static Leaderboard _leaderboard;
        private static bool _gameOver = false;

        static void Main(string[] args)
        {
            Console.Title = "Крестики-нолики - Сервер";
            Console.WriteLine("=== Сервер Крестики-нолики ===");
            Console.WriteLine("Игрок 1 = X, Игрок 2 = O");

            LoadLeaderboard();

            int port = 8888;
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            Console.WriteLine($"Сервер запущен на порту {port}");
            Console.WriteLine("Ожидание игроков...");
            Console.WriteLine();

            while (true)
            {
                // Ждём первого игрока
                if (_client1 == null || !_client1.Connected)
                {
                    Console.WriteLine("Ожидание Игрока 1 (X)...");
                    _client1 = _listener.AcceptTcpClient();
                    Console.WriteLine($"Игрок 1 (X) подключился: {_client1.Client.RemoteEndPoint}");
                    SendMessage(_client1.GetStream(), "WELCOME;1;X");
                }

                // Ждём второго игрока
                if (_client2 == null || !_client2.Connected)
                {
                    Console.WriteLine("Ожидание Игрока 2 (O)...");
                    _client2 = _listener.AcceptTcpClient();
                    Console.WriteLine($"Игрок 2 (O) подключился: {_client2.Client.RemoteEndPoint}");
                    SendMessage(_client2.GetStream(), "WELCOME;2;O");
                }

                // Оба подключены - начинаем игру
                if (_client1.Connected && _client2.Connected && _gameOver == false)
                {
                    StartNewGame();
                    PlayGame();
                }

                Thread.Sleep(100);
            }
        }

        static void StartNewGame()
        {
            ResetBoard();
            _currentPlayer = 0;
            _gameOver = false;

            SendMessage(_client1.GetStream(), "START;X;O");
            SendMessage(_client2.GetStream(), "START;O;X");

            Console.WriteLine();
            Console.WriteLine("=== Новая игра ===");
            Console.WriteLine("Игрок 1 (X) vs Игрок 2 (O)");
            Console.WriteLine("Ход Игрока 1 (X)");
        }

        static void PlayGame()
        {
            while (!_gameOver && _client1.Connected && _client2.Connected)
            {
                TcpClient currentClient = _currentPlayer == 0 ? _client1 : _client2;
                string playerSymbol = _currentPlayer == 0 ? "X" : "O";

                try
                {
                    var stream = currentClient.GetStream();

                    if (stream.DataAvailable)
                    {
                        string move = ReceiveMessage(stream);

                        if (string.IsNullOrEmpty(move))
                        {
                            Console.WriteLine($"Игрок {_currentPlayer + 1} отключился");
                            break;
                        }

                        string[] parts = move.Split(';');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                        {
                            int index = x * 3 + y;

                            if (index >= 0 && index < 9 && _board[index] == '\0')
                            {
                                char symbol = _currentPlayer == 0 ? 'X' : 'O';
                                _board[index] = symbol;

                                Console.WriteLine($"Игрок {_currentPlayer + 1} ({playerSymbol}): ход ({x},{y})");

                                // Отправляем ход обоим
                                SendMessage(_client1.GetStream(), $"MOVE;{_currentPlayer};{x};{y}");
                                SendMessage(_client2.GetStream(), $"MOVE;{_currentPlayer};{x};{y}");

                                // Проверяем победу
                                char winner = CheckWinner();
                                if (winner != '\0')
                                {
                                    Console.WriteLine($"ПОБЕДА: Игрок {_currentPlayer + 1} ({playerSymbol})!");
                                    SendMessage(_client1.GetStream(), $"WIN;{_currentPlayer}");
                                    SendMessage(_client2.GetStream(), $"WIN;{_currentPlayer}");
                                    UpdateLeaderboard($"Игрок{_currentPlayer + 1}", "Win");
                                    _gameOver = true;
                                    Thread.Sleep(3000);
                                    break;
                                }

                                // Проверяем ничью
                                if (IsBoardFull())
                                {
                                    Console.WriteLine("НИЧЬЯ!");
                                    SendMessage(_client1.GetStream(), "DRAW");
                                    SendMessage(_client2.GetStream(), "DRAW");
                                    UpdateLeaderboard("Игрок1", "Draw");
                                    UpdateLeaderboard("Игрок2", "Draw");
                                    _gameOver = true;
                                    Thread.Sleep(3000);
                                    break;
                                }

                                // Переключаем игрока
                                _currentPlayer = 1 - _currentPlayer;
                                Console.WriteLine($"Ход Игрока {_currentPlayer + 1} ({(_currentPlayer == 0 ? "X" : "O")})");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    break;
                }

                Thread.Sleep(50);
            }

            // Сбрасываем для новой игры
            if (_gameOver)
            {
                Console.WriteLine("Игра завершена. Начинаем новую...");
                _gameOver = false;
            }
        }

        static void ResetBoard()
        {
            for (int i = 0; i < 9; i++)
                _board[i] = '\0';
        }

        static char CheckWinner()
        {
            int[][] lines = new int[][]
            {
                new int[] { 0,1,2 }, new int[] { 3,4,5 }, new int[] { 6,7,8 },
                new int[] { 0,3,6 }, new int[] { 1,4,7 }, new int[] { 2,5,8 },
                new int[] { 0,4,8 }, new int[] { 2,4,6 }
            };

            foreach (var line in lines)
            {
                if (_board[line[0]] != '\0' &&
                    _board[line[0]] == _board[line[1]] &&
                    _board[line[1]] == _board[line[2]])
                    return _board[line[0]];
            }
            return '\0';
        }

        static bool IsBoardFull()
        {
            foreach (char c in _board)
                if (c == '\0') return false;
            return true;
        }

        static void SendMessage(NetworkStream stream, string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            catch { }
        }

        static string ReceiveMessage(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
        }

        static void LoadLeaderboard()
        {
            if (File.Exists("leaderboard.json"))
            {
                string json = File.ReadAllText("leaderboard.json");
                _leaderboard = JsonConvert.DeserializeObject<Leaderboard>(json) ?? new Leaderboard();
            }
            else
            {
                _leaderboard = new Leaderboard();
            }
        }

        static void UpdateLeaderboard(string playerName, string result)
        {
            var entry = _leaderboard.Entries.Find(e => e.PlayerName == playerName);
            if (entry == null)
            {
                entry = new LeaderboardEntry { PlayerName = playerName };
                _leaderboard.Entries.Add(entry);
            }

            entry.GamesPlayed++;
            if (result == "Win") entry.Wins++;
            else if (result == "Draw") entry.Draws++;
            else entry.Losses++;

            string json = JsonConvert.SerializeObject(_leaderboard, Formatting.Indented);
            File.WriteAllText("leaderboard.json", json);
        }
    }

    public class Leaderboard
    {
        public List<LeaderboardEntry> Entries { get; set; } = new List<LeaderboardEntry>();
    }

    public class LeaderboardEntry
    {
        public string PlayerName { get; set; }
        public int GamesPlayed { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
    }
}