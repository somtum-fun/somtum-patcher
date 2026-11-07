using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OsuPatcher.Runtime.Resolver
{
    internal static class SpriteManager
    {
        private static FieldInfo _spriteManagerWidescreenField;
        private static MethodInfo _addMethod;
        private static FieldInfo _spriteListField;
        private static Type _spriteManagerType;
        private static List<FieldInfo> _spriteManagerFields;
        private static FieldInfo _targetSpriteManagerField;

        /// <param name="playerType">osu!'s Player class (not the instance's type: a ReplayWatcher or
        /// PlayerVs wouldn't show Player's private sprite managers).</param>
        public static void AddToWidescreen(object playerInstance, Type playerType, object drawableInstance)
        {
            if (_spriteManagerWidescreenField == null)
            {
                var managerCandidates = playerType
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => !f.FieldType.IsPrimitive && !f.FieldType.IsEnum && f.FieldType != typeof(string))
                    .Select(f => new
                    {
                        Field = f,
                        Instance = f.GetValue(playerInstance),
                    })
                    .Where(x => x.Instance != null)
                    .Select(x => new
                    {
                        x.Field,
                        Type = x.Instance.GetType(),
                        Score = GetManagerScore(x.Instance.GetType(), drawableInstance.GetType()),
                    })
                    .Where(x => x.Score >= 100)
                    .OrderByDescending(x => x.Score)
                    .ToList();

                if (managerCandidates.Count == 0)
                    return;

                _spriteManagerWidescreenField = managerCandidates[0].Field;
                _spriteManagerType = managerCandidates[0].Type;
                ResolveAddTarget(drawableInstance.GetType());
                _spriteManagerFields = managerCandidates
                    .Where(x => x.Type == _spriteManagerType)
                    .Select(x => x.Field)
                    .ToList();
                _targetSpriteManagerField = _spriteManagerFields.Last();
            }

            if (_targetSpriteManagerField != null && _addMethod != null)
            {
                var managerInstance = _targetSpriteManagerField.GetValue(playerInstance);
                if (managerInstance == null)
                    return;

                _addMethod.Invoke(managerInstance, new[] { drawableInstance });
            }
            else if (_targetSpriteManagerField != null && _spriteListField != null)
            {
                var managerInstance = _targetSpriteManagerField.GetValue(playerInstance);
                var list = _spriteListField.GetValue(managerInstance);
                var add = list?.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                add?.Invoke(list, new[] { drawableInstance });
            }
        }

        private static void ResolveAddTarget(Type drawableInstanceType)
        {
            var listFields = _spriteManagerType
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                .Select(f => new
                {
                    Field = f,
                    ItemType = f.FieldType.GetGenericArguments()[0],
                })
                .Where(x => x.ItemType.IsAssignableFrom(drawableInstanceType))
                .ToList();

            _spriteListField = listFields.FirstOrDefault()?.Field;

            var drawableBaseType = listFields.FirstOrDefault()?.ItemType;

            _addMethod = _spriteManagerType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => !m.IsGenericMethodDefinition && m.ReturnType == typeof(void))
                .Select(m => new
                {
                    Method = m,
                    Parameters = m.GetParameters(),
                })
                .Where(x => x.Parameters.Length == 1)
                .Where(x => x.Parameters[0].ParameterType.IsAssignableFrom(drawableInstanceType))
                .OrderByDescending(x => drawableBaseType != null && x.Parameters[0].ParameterType == drawableBaseType)
                .ThenBy(x => x.Parameters[0].ParameterType == typeof(object))
                .Select(x => x.Method)
                .FirstOrDefault();
        }

        private static int GetManagerScore(Type managerType, Type drawableInstanceType)
        {
            int score = 0;

            if (managerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(f => f.FieldType.IsGenericType &&
                    f.FieldType.GetGenericTypeDefinition() == typeof(List<>) &&
                    f.FieldType.GetGenericArguments()[0].IsAssignableFrom(drawableInstanceType)))
            {
                score += 10;
            }

            if (managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m =>
                {
                    if (m.IsGenericMethodDefinition || m.ReturnType != typeof(void))
                        return false;

                    var parameters = m.GetParameters();
                    return parameters.Length == 1 &&
                        parameters[0].ParameterType.IsAssignableFrom(drawableInstanceType);
                }))
            {
                score += 20;
            }

            if (managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(bool)))
            {
                score += 40;
            }

            if (managerType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Count(f => f.FieldType == typeof(float)) >= 3)
            {
                score += 40;
            }

            if (managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(m => m.GetParameters().Length == 0 && m.ReturnType == typeof(void)))
            {
                score += 1;
            }

            return score;
        }
    }
}
