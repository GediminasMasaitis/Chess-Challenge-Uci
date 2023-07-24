﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ChessChallenge.API;
using Timer = ChessChallenge.API.Timer;

public class MyBot : IChessBot
{
    private const int Inf = 2000000;
    private const int Mate = 1000000;

    struct TT_entry {
        public ulong key;
        public Move move;
        public int depth, score;
        public byte flag; // 1 = Upper, 2 = Lower, 3 = Exact
        public TT_entry(ulong _key, Move _move, int _depth, int _score, byte _flag) {
            key = _key; move = _move; depth = _depth; score = _score; flag = _flag;
        }
    }

    const int entries = 1048576;
    TT_entry[] TT = new TT_entry[entries];

    int[] pieceValues = { 0, 151, 419, 458, 731, 1412, 0 };

    // PSTs are encoded with the following format:
    // Every rank or file is encoded as a byte, with the first rank/file being the LSB and the last rank/file being the MSB.
    // For every value to fit inside a byte, the values are divided by 2, and multiplication inside evaluation is needed.
    ulong[] pstRanks = {0, 32973249741911296, 16357091511995071475, 17581496622553367027, 724241724997039354, 432919517870226424, 17729000522595302646 };
    ulong[] pstFiles = {0, 17944594909985834239, 17438231369917791979, 17799354947352068342, 17580088143863153148, 217585671819360496, 17944030877684269297 };

    private int Evaluate(Board board)
    {
        int score = 0;
        for (var color = 0; color < 2; color++)
        {
            var isWhite = color == 0;
            for (var piece = PieceType.Pawn; piece <= PieceType.King; piece++)
            {
                var pieceIndex = (int)piece;
                var bitboard = board.GetPieceBitboard(piece, isWhite);

                while (bitboard != 0)
                {
                    var sq = BitOperations.TrailingZeroCount(bitboard);
                    bitboard &= bitboard - 1;

                    if (color == 1)
                    {
                        sq ^= 56;
                    }

                    var rank = sq >> 3;
                    var file = sq & 7;

                    // Material
                    score += pieceValues[pieceIndex];

                    // Rank PST
                    var rankScore = (sbyte)((pstRanks[pieceIndex] >> (rank * 8)) & 0xFF) * 2;
                    score += rankScore;

                    // File PST
                    var fileScore = (sbyte)((pstFiles[pieceIndex] >> (file * 8)) & 0xFF) * 2;
                    score += fileScore;
                }
            }

            score = -score;
        }

        if (!board.IsWhiteToMove)
        {
            score = -score;
        }

        return score;
    }

    private int Search(Board board, Timer timer, int totalTime, int ply, int depth, int alpha, int beta, out Move bestMove)
    {
        ulong tt_key = board.ZobristKey;

        // Repetition detection
        if (ply > 0 && board.IsRepeatedPosition())
        {
            return 0;
        }

        // If we are in check, we should search deeper
        if (board.IsInCheck())
            depth++;

        var inQsearch = (depth <= 0);

        TT_entry tte = TT[tt_key % entries];

        if (ply > 0 && tte.key == tt_key && tte.depth >= depth
            && (tte.flag == 3 || (tte.flag == 2 && tte.score >= beta) || (tte.flag == 1 && tte.score <= alpha)))
            return tte.score;

        var in_qsearch = (depth <= 0);
        var bestScore = -Inf;

        if (inQsearch)
        {
            var staticEval = Evaluate(board);
            if (staticEval >= beta)
                return staticEval;

            if (staticEval > alpha)
                alpha = staticEval;
        }

        // MVV-LVA ordering, TT move first
        var moves = board.GetLegalMoves(inQsearch).OrderByDescending(move => move == tte.move).ThenByDescending(move => move.CapturePieceType).ThenBy(move => move.MovePieceType);

        var movesEvaluated = 0;
        bestMove = tte.move;
        byte flag = 1;

        // Loop over each legal move
        foreach (var move in moves)
        {
            // If we are out of time, stop searching
            if (depth > 2 && timer.MillisecondsElapsedThisTurn * 30 > totalTime)
            {
                return bestScore;
            }

            board.MakeMove(move);
            var score = -Search(board, timer, totalTime, ply + 1, depth - 1, -beta, -alpha, out _);
            board.UndoMove(move);

            // Count the number of moves we have evaluated for detecting mates and stalemates
            movesEvaluated++;

            // If the move is better than our current best, update our best move
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;

                // If the move is better than our current alpha, update alpha
                if (score > alpha)
                {
                    alpha = score;
                    flag = 3;

                    // If the move is better than our current beta, we can stop searching
                    if (score >= beta)
                    {
                        flag = 2;
                        break;
                    }
                }
            }
        }

        if (movesEvaluated == 0) 
            return inQsearch ? bestScore : board.IsInCheck() ? -Mate : 0;

        TT[tt_key % entries] = new TT_entry(tt_key, bestMove == Move.NullMove ? tte.move : bestMove, depth, bestScore, flag);

        return bestScore;
    }

    public Move Think(Board board, Timer timer)
    {
        var totalTime = timer.MillisecondsRemaining;

        var bestMove = Move.NullMove;
        // Iterative deepening
        for (var depth = 1; depth < 128; depth++)
        {
            var score = Search(board, timer, totalTime, 0, depth, -Inf, Inf, out var move);

            // If we are out of time, we cannot trust the move that was found during this iteration
            if (timer.MillisecondsElapsedThisTurn * 30 > totalTime)
            {
                break;
            }

            bestMove = move;

            // For debugging purposes, can be removed if lacking tokens
            // Move is not printed in the usual pv format, because the API does not support easy conversion to UCI notation
            Console.WriteLine($"info depth {depth} cp {score} time {timer.MillisecondsElapsedThisTurn} {bestMove}");
        }

        return bestMove;
    }
}
