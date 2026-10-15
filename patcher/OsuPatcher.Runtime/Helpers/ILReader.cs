using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace OsuPatcher.Runtime.Helpers
{
    internal class ILReader
    {
        private readonly byte[] _ilInstructions;
        private int _pos;

        private static readonly Dictionary<(byte prefix, byte code), OpCode> Opcodes;

        static ILReader()
        {
            Opcodes = new Dictionary<(byte prefix, byte code), OpCode>();

            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var opcode = (OpCode)field.GetValue(null);
                if (opcode.OpCodeType == OpCodeType.Nternal) continue;

                var prefix = (byte)(opcode.Size == 1 ? 0 : 0xFE);
                var code = (byte)(opcode.Value & 0xFF);
                Opcodes[(prefix, code)] = opcode;
            }
        }

        public ILReader(byte[] ilInstructions) => _ilInstructions = ilInstructions ?? throw new ArgumentNullException(nameof(ilInstructions));

        public IEnumerable<OpCode> GetOpCodes()
        {
            foreach (var instruction in GetInstructions())
                yield return instruction.OpCode;
        }

        /// <summary>One IL instruction: its opcode and its operand (a token, constant or branch offset), 0 if none.</summary>
        internal readonly struct Instruction
        {
            public readonly OpCode OpCode;
            public readonly long Operand;
            public Instruction(OpCode opCode, long operand) { OpCode = opCode; Operand = operand; }
        }

        /// <summary>
        /// Decodes the method body. Stops quietly at a byte that isn't an opcode (data or a body the
        /// obfuscator mangled) instead of throwing: the callers scan every method in osu!, and one
        /// odd body must not end the whole scan.
        /// </summary>
        public IEnumerable<Instruction> GetInstructions()
        {
            var il = _ilInstructions;
            while (_pos < il.Length)
            {
                var currentByte = il[_pos++];
                var prefix = (byte)(currentByte == 0xFE ? 0xFE : 0);
                if (prefix == 0xFE && _pos >= il.Length)
                    yield break;
                var code = prefix == 0xFE ? il[_pos++] : currentByte;

                if (!Opcodes.TryGetValue((prefix, code), out var op))
                    yield break;

                long operand = 0;
                int size;
                switch (op.OperandType)
                {
                    case OperandType.InlineSwitch:
                        // A 4-byte target count, then that many 4-byte targets. (This used to skip
                        // 1 + 4n bytes, so every switch left the decoder 3 bytes out of step.)
                        if (_pos + 4 > il.Length) yield break;
                        long count = (uint)(il[_pos] | (il[_pos + 1] << 8) | (il[_pos + 2] << 16) | (il[_pos + 3] << 24));
                        if (_pos + 4 + count * 4 > il.Length) yield break;
                        _pos += 4 + (int)count * 4;
                        yield return new Instruction(op, count);
                        continue;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        size = 8;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineMethod:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.InlineSig:
                    case OperandType.ShortInlineR:
                        size = 4;
                        break;
                    case OperandType.InlineVar:
                        size = 2;
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        size = 1;
                        break;
                    case OperandType.InlineNone:
                        size = 0;
                        break;
                    default:
                        yield break;
                }

                if (_pos + size > il.Length)
                    yield break;
                for (int i = 0; i < size; i++)
                    operand |= (long)il[_pos + i] << (8 * i);
                if (size == 1 && op.OperandType == OperandType.ShortInlineI)
                    operand = (sbyte)operand;
                else if (size == 4 && op.OperandType != OperandType.ShortInlineR)
                    operand = (int)operand;
                _pos += size;

                yield return new Instruction(op, operand);
            }
        }
    }
}
