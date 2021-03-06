using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MbCache.Core;
using MbCache.Core.Events;
using MbCache.Logic;

namespace MbCache.Configuration
{
	/// <summary>
	/// Base class for users to override to implement
	/// their own logic for building cache keys
	/// 
	/// Will build cache key in format
	/// Type
	/// Type|Component
	/// Type|Component|Method|ParamType1|ParamType2
	/// Type|Component|Method|ParamType1|ParamType2|$ParamValue1$ParamValue2
	/// </summary>
	[Serializable]
	public abstract class CacheKeyBase : ICacheKey
	{
		private const string suspisiousParam =
			"Cache key of type {0} equals its own type name. Possible bug in your ICacheKey implementation.";
		private static readonly Regex findSeperator = new Regex(@"\" + separator, RegexOptions.Compiled);
		private const string separator = "|";
		private const string separatorForParameters = "$";

		private IEnumerable<IEventListener> _eventListeners;

		public void Initialize(IEnumerable<IEventListener> eventListeners)
		{
			_eventListeners = eventListeners;
		}

		public string RemoveKey(ComponentType type)
		{
			return type.ToString();
		}

		public string RemoveKey(ComponentType type, ICachingComponent component)
		{
			return string.Concat(RemoveKey(type), separator, component.UniqueId);
		}

		public string RemoveKey(ComponentType type, ICachingComponent component, MethodInfo method)
		{
			return stringBuilderForMethodBody(type, component, method).ToString();
		}

		public string RemoveKey(ComponentType type, ICachingComponent component, MethodInfo method, IEnumerable<object> parameters)
		{
			var ret = stringBuilderForMethodBody(type, component, method);
			ret.Append(separator);
			foreach (var parameter in parameters)
			{
				ret.Append(separatorForParameters);
				var parameterKey = ParameterValue(parameter);
				if (parameterKey == null)
					return null;
				checkIfSuspiousParameter(parameter, parameterKey);
				ret.Append(parameterKey);
			}

			return ret.ToString();
		}

		private StringBuilder stringBuilderForMethodBody(ComponentType type, ICachingComponent component, MethodInfo method)
		{
			var ret = new StringBuilder(RemoveKey(type, component));
			ret.Append(separator);
			ret.Append(method.Name);
			foreach (var parameter in method.GetParameters())
			{
				ret.Append(separatorForParameters);
				ret.Append(parameter.ParameterType);
			}
			return ret;
		}

		public KeyAndItsDependingKeys GetAndPutKey(ComponentType type, ICachingComponent component, MethodInfo method, IEnumerable<object> parameters)
		{
			var startKey = RemoveKey(type, component, method, parameters);
			if (startKey == null)
				return new KeyAndItsDependingKeys();

			var scope = Scope();
			var completeKey = scope == null ? 
				startKey : 
				string.Concat(startKey, separator, scope);
			return new KeyAndItsDependingKeys(completeKey, () => allRemoveKeys(completeKey));
		}

		private static IEnumerable<string> allRemoveKeys(string getAndPutKey)
		{
			var matches = findSeperator.Matches(getAndPutKey);
			return from Match match in matches select getAndPutKey.Substring(0, match.Index);
		}

		private void checkIfSuspiousParameter(object parameter, string parameterKey)
		{
			if (parameter != null)
			{
				var parameterType = parameter.GetType();
				if (parameterKey.Equals(parameterType.ToString()))
				{
					foreach (var eventListener in _eventListeners)
					{
						eventListener.Warning(string.Format(suspisiousParam, parameterType));
					}
				}
			}
		}

		/// <summary>
		/// Adds a string at the beginning of the cache key.
		/// Can be used to have different cache entries based on some logic,
		/// eg in tenant scenarios.
		/// </summary>
		protected virtual string Scope()
		{
			return null;
		}

		/// <summary>
		/// Adds string to cache key for parameter values  
		/// </summary>
		/// <param name="parameter">The parameter.</param>
		/// <returns>
		/// A string reporesentation of the parameter.
		/// </returns>
		protected abstract string ParameterValue(object parameter);
	}
}