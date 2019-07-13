using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WSQ.Common
{
	/// <summary>
	/// 用MemoryCache缓存函数的执行结果。可以减少对db和redis的读取，也可以减少任何其他函数的执行。
	/// 用例：var preData = CacheHelper.WithCache("userstock_GetUSPreDataTradingCode_" + stockCode, ()=>Redis.QuoteData.GetUSPreDataTradingCode(stockCode), 5000);
	/// </summary>
	public static class CacheHelper
	{
		private static MemoryCache mc = new MemoryCache(Guid.NewGuid().ToString());

		/// <summary>
		/// 用MemoryCache缓存getData的结果。Func部分可以 ()=>getData(a,b,c)
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key">注意key要够独特</param>
		/// <param name="getData"></param>
		/// <param name="arg"></param>
		/// <param name="cacheMilli"></param>
		/// <returns></returns>
		public static T WithCache<T>(string key, Func<T> getData, double cacheMilli = 1000) where T : new()
		{
			object obj = mc.Get(key);
			if (obj != null)
			{
				return (T)obj;
			}
			else
			{
				T dd = getData();
				if (dd == null)
				{
					dd = new T();
				}
				mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));
				return dd;
			}
		}

		private class CacheInfo
		{
			public string key;
			public DateTime lastCacheTime;
			public double cacheMilli;
			/// <summary>
			/// 控制并发程度，不需要严格的线程安全
			/// </summary>
			public bool isRefreshing;

			/// <summary>
			/// 是否需要提前刷新
			/// </summary>
			/// <returns></returns>
			public bool NeedPredicateRefresh()
			{
				var milliExist = (DateTime.Now - this.lastCacheTime).TotalMilliseconds;

				//正在提前刷新的别再提前刷新。
				//缓存太短的数据没必要提前刷新。
				//快过期了才提前刷新。
				bool needRefresh = !isRefreshing && this.cacheMilli > 500 && milliExist > (this.cacheMilli * 0.8);
				return needRefresh;
			}
		}
		private static ConcurrentDictionary<string, CacheInfo> dicCacheInfo = new ConcurrentDictionary<string, CacheInfo>();

		/// <summary>
		/// 用MemoryCache缓存getData的结果。Func部分可以 ()=>getData(a,b,c)。
		/// 在缓存快过期的时候，按需启动线程去提前刷新缓存，以尽量避免Func太慢导致的偶发响应变慢。
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key">注意key要够独特</param>
		/// <param name="getData"></param>
		/// <param name="arg"></param>
		/// <param name="cacheMilli"></param>
		/// <returns></returns>
		public static T WithCacheRefresh<T>(string key, Func<T> getData, double cacheMilli = 1000) where T : new()
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
				//cache存在，看看要不要提前刷新
				if (info.NeedPredicateRefresh())
				{
					//立刻设置一下isRefreshing，避免起太多线程
					info.isRefreshing = true;

					Thread th = new Thread(new ThreadStart(() => CheckRefreshCache(key, getData, cacheMilli)));
					th.Name = "WithCacheConcurrent_refresh";
					th.IsBackground = true;
					th.Start();
				}

				return (T)obj;
			}
			else
			{
				T dd = getData();
				if (dd == null)
				{
					dd = new T();
				}
				mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));

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
		private static void CheckRefreshCache<T>(string key, Func<T> getData, double cacheMilli) where T : new()
		{
			dicCacheInfo.TryGetValue(key, out CacheInfo info);
			if (info == null)
			{
				return;
			}

			try
			{
				T dd = getData();
				if (dd == null)
				{
					dd = new T();
				}
				mc.Set(key, dd, DateTime.Now.AddMilliseconds(cacheMilli));

				info.lastCacheTime = DateTime.Now;
				info.cacheMilli = cacheMilli;
			}
			catch (Exception)
			{
			}
			info.isRefreshing = false;
		}
	}
}
