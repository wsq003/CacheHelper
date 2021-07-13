using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Utils
{
	/// <summary>
	/// Cache result of functions, to boost performance.
	/// </summary>
	public static class CacheHelper
	{
		private static MemoryCache mc = new MemoryCache(new MemoryCacheOptions());

		private static string nullstr = "ajktNullHolder";

		/// <summary>
		/// Cache result of functions, to boost performance.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key"></param>
		/// <param name="getData">can ()=>getData(a,b,c)</param>
		/// <param name="arg"></param>
		/// <param name="cacheMilli"></param>
		/// <returns></returns>
		public static T? WithCache<T>(string key, Func<T> getData, double cacheMilli = 1000, bool cacheNullResult = false)
		{
			object obj = mc.Get(key);
			if (obj != null)
			{
				if (obj is string)
				{
					if (obj as string == nullstr)
					{
						return default(T);
					}
				}
				return (T)obj;
			}
			else
			{
				T? dd = getData();
				if (dd == null)
				{
					if (cacheNullResult)
					{
						mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
						dd = default(T);
					}
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}
				return dd;
			}
		}

		public static async Task<T?> WithCacheAsync<T>(string key, Func<Task<T?>> getData, double cacheMilli = 1000, bool cacheNullResult = false)
		{
			object obj = mc.Get(key);
			if (obj != null)
			{
				if (obj is string)
				{
					if (obj as string == nullstr)
					{
						return default(T);
					}
				}
				return (T)obj;
			}
			else
			{
				var dd = await getData();
				if (dd == null)
				{
					if (cacheNullResult)
					{
						mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
						dd = default(T);
					}
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}
				return dd;
			}
		}

		public static void RemoveCache(string key)
		{
			mc.Remove(key);
		}

		#region WithCacheRefresh

		private class CacheInfo
		{
			public string key = "";
			public DateTime lastCacheTime;
			public double cacheMilli;

			public volatile bool isRefreshing;

			/// <summary>
			/// 
			/// </summary>
			/// <returns></returns>
			public bool NeedPredicateRefresh()
			{
				var milliExist = (DateTime.Now - this.lastCacheTime).TotalMilliseconds;

				bool needRefresh = !isRefreshing && this.cacheMilli > 500 && milliExist > (this.cacheMilli * 0.8);
				return needRefresh;
			}
		}
		private static ConcurrentDictionary<string, CacheInfo> dicCacheInfo = new ConcurrentDictionary<string, CacheInfo>();

		/// <summary>
		/// Cache result of functions, to boost performance. Will try refresh cache before expire.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key"></param>
		/// <param name="getData">can be ()=>getData(a,b,c)</param>
		/// <param name="arg"></param>
		/// <param name="cacheMilli"></param>
		/// <returns></returns>
		public static T? WithCacheRefresh<T>(string key, Func<T> getData, double cacheMilli = 1000, bool cacheNullResult = false)
		{
			var info = dicCacheInfo.GetOrAdd(key, new CacheInfo()
			{
				key = key,
				lastCacheTime = new DateTime(),
				cacheMilli = cacheMilli,
				isRefreshing = false
			});

			object obj = mc.Get(key);
			if (obj != null)
			{
				if (info.NeedPredicateRefresh())
				{
					info.isRefreshing = true;

					Thread th = new Thread(new ThreadStart(() => CheckRefreshCache(key, getData, cacheMilli)));
					th.Name = "WithCacheConcurrent_refresh";
					th.IsBackground = true;
					th.Start();
				}

				if (obj is string)
				{
					if (obj as string == nullstr)
					{
						return default(T);
					}
				}
				return (T)obj;
			}
			else
			{
				T? dd = getData();
				if (dd == null)
				{
					mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
					dd = default(T);
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}

				info.lastCacheTime = DateTime.Now;
				info.cacheMilli = cacheMilli;

				return dd;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key"></param>
		/// <param name="getData"></param>
		/// <param name="cacheMilli"></param>
		private static void CheckRefreshCache<T>(string key, Func<T> getData, double cacheMilli)
		{
			dicCacheInfo.TryGetValue(key, out CacheInfo? info);
			if (info == null)
			{
				return;
			}

			try
			{
				T? dd = getData();
				if (dd == null)
				{
					mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
					dd = default(T);
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}

				info.lastCacheTime = DateTime.Now;
				info.cacheMilli = cacheMilli;
			}
			catch (Exception)
			{
			}
			info.isRefreshing = false;
		}

		public static async Task<T?> WithCacheRefreshAsync<T>(string key, Func<Task<T?>> getData, double cacheMilli = 1000, bool cacheNullResult = false)
		{
			var info = dicCacheInfo.GetOrAdd(key, new CacheInfo()
			{
				key = key,
				lastCacheTime = new DateTime(),
				cacheMilli = cacheMilli,
				isRefreshing = false
			});

			object obj = mc.Get(key);
			if (obj != null)
			{
				if (info.NeedPredicateRefresh())
				{
					info.isRefreshing = true;

					Thread th = new Thread(new ThreadStart(async () => await CheckRefreshCacheAsync(key, getData, cacheMilli, cacheNullResult)));
					th.Name = "WithCacheConcurrent_refresh";
					th.IsBackground = true;
					th.Start();
				}

				if (obj is string)
				{
					if (obj as string == nullstr)
					{
						return default(T);
					}
				}
				return (T)obj;
			}
			else
			{
				T? dd = await getData();
				if (dd == null)
				{
					if (cacheNullResult)
					{
						mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
					}
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}

				info.lastCacheTime = DateTime.Now;
				info.cacheMilli = cacheMilli;

				return dd;
			}
		}


		private static async Task CheckRefreshCacheAsync<T>(string key, Func<Task<T>> getData, double cacheMilli, bool cacheNullResult)
		{
			dicCacheInfo.TryGetValue(key, out CacheInfo? info);
			if (info == null)
			{
				return;
			}

			try
			{
				T? dd = await getData();
				if (dd == null)
				{
					if (cacheNullResult)
					{
						mc.Set(key, nullstr, DateTime.Now.AddMilliseconds(cacheMilli));
					}
				}
				else
				{
					mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				}

				info.lastCacheTime = DateTime.Now;
				info.cacheMilli = cacheMilli;
			}
			catch (Exception)
			{
			}
			info.isRefreshing = false;
		}

		#endregion
	}
}
