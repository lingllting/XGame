using UnityEngine;
using System;
using System.Diagnostics;

public class GameDriver : MonoBehaviour 
{
	//有效游戏逻辑帧帧数
	public static int sm_iValidGameFrameCount = 0;
	//游戏逻辑帧总帧数
	public static int sm_iTotalGameFrameCount = 0;
	//初始帧锁定毫秒数
	public const int INITIAL_LOCKSTEP_LENGTH = 100;
	//初始游戏逻辑帧毫秒数
	public const int INITIAL_GAMEFRAME_LENGTH = 20;
	//当前的锁定帧ID
	public int LockStepTurnID
	{
		get{return _iLockStepTurnID;}
	}
	//服务器的锁定帧
	public int m_iServerTurnID;
	//单例
	public static GameDriver Instance
	{
		get{return _instance;}
	}

	public float deltaTime
	{
		get
		{
			if (_iGameFrameLength == 0)
			{
				return INITIAL_GAMEFRAME_LENGTH * 0.001f;
			}
			return _iGameFrameLength * 0.001f;
		}
	}

	public float time
	{
		get
		{
			if (_bBeginLockStep)
			{
				return INITIAL_GAMEFRAME_LENGTH * sm_iValidGameFrameCount * 0.001f;
			}
			else
			{
				return INITIAL_GAMEFRAME_LENGTH * sm_iTotalGameFrameCount * 0.001f;
			}
		}
	}

	public Stopwatch GameTurnSW
	{
		get{return _gameTurnSW;}
	}
	public Stopwatch PriorNetSW
	{
		get{return _priorNetSW;}
	}
	public Stopwatch CurrentNetSW
	{
		get{return _currentNetSW;}
	}

	private static GameDriver _instance = null;
	//每秒游戏逻辑帧数
	private int _iGameFramesPerSecond = 0;
	//游戏逻辑帧毫秒数
	private int _iGameFrameLength = 0;
	//帧锁定游戏逻辑帧数
	private int _iGameFramesPerLockStepTurn = 0;
	//累积时间
	private int _iAccumilatedTime = 0;
	//当前锁定帧时间内的游戏帧计数
	private int _iGameFrameInLockStepTurn = 0;
	//当前锁定帧ID
	private int _iLockStepTurnID = 0;
	//是否开始帧锁定
	private bool _bBeginLockStep = false;
	//服务器当前轮次包是否发送
	private bool _bCurrentServerTurnPackageSended = false;
	//游戏帧监视器
	private Stopwatch _gameTurnSW = new Stopwatch();
	//游戏网络监视器
	private Stopwatch _priorNetSW = new Stopwatch();
	private Stopwatch _currentNetSW = new Stopwatch();

	//服务器延迟
	private bool _bIsServerDelay = false;
	private int _iServerDelayTime = 0;

	private long _lCurrentGameFrameRuntime = 0;
	private bool _bIsStart = true;

	void Awake()
	{
		_instance = this;
		SetGameFrameRate(50);
	}
	
	void Update()
	{
		if (_bIsServerDelay)
		{
			_iServerDelayTime += Convert.ToInt32((Time.deltaTime * 1000));
			if (_iServerDelayTime > INITIAL_LOCKSTEP_LENGTH)
			{
				_bIsServerDelay = false;
				_iServerDelayTime = 0;
			}
			else
			{
				return;
			}
		}

		if (!_bIsStart)
		{
			return;
		}

		_iAccumilatedTime = _iAccumilatedTime + Convert.ToInt32((Time.deltaTime * 1000));
		
		while (_iAccumilatedTime > _iGameFrameLength)
		{
			sm_iTotalGameFrameCount++;

			if (!_bBeginLockStep)
			{
				//TODO Game logic Update
				// //关卡AI刷新
				// TollgateAI.Instance.DoUpdate();
				// //游戏世界刷新
				// GameWorld.Instance.DoUpdate();
				// //弹药刷新
				// AmmoManager.Instance.DoUpdate();
				// //地面BUFF刷新
				// TerrainBufferMgr.Instance.DoUpdate();
				// //角色Buff刷新
				// RoleBuffManager.Instance.DoUpdate();
				// //怪物生成器刷新
				// MonsterWaveCreator.Instance.DoUpdate();
				// //计时器刷新
				// vp_Timer.Instance.DoUpdate();
				// //寻路
				// AIGridPathFind.Instance.DoUpdate();
			}
			else
			{
				GameFrameRun();
			}
			_iAccumilatedTime = _iAccumilatedTime - _iGameFrameLength;
		}

		//差3帧，补帧
		if (GameDriver.Instance.m_iServerTurnID > GameDriver.Instance.LockStepTurnID + 2)
		{
			int loopNum = 0;
			while (GameDriver.Instance.m_iServerTurnID > GameDriver.Instance.LockStepTurnID)
			{
				loopNum++;
				if (loopNum > 10)
				{
					break;
				}
				GameFrameRun();
			}
		}
	}

	/// <summary>
	/// 构建游戏驱动.
	/// </summary>
	public static void BuildDriver()
	{
		if (_instance != null)
		{
			return;
		}

		GameObject obj = new GameObject("GameDriver");
		DontDestroyOnLoad(obj);
		_instance = obj.AddComponent<GameDriver>();
	}

	/// <summary>
	/// 删除游戏驱动.
	/// </summary>
	public void DestroyBuilder()
	{
		Destroy(this.gameObject);
		_instance = null;
		sm_iValidGameFrameCount = 0;
		sm_iTotalGameFrameCount = 0;
	}

	/// <summary>
	/// 开始游戏驱动.
	/// </summary>
	public void StartDriver()
	{
		_bIsStart = true;
		CancelInvoke(nameof(StartTimer));
	}

	/// <summary>
	/// 停止游戏驱动.
	/// </summary>
	public void StopDriver()
	{
		_bIsStart = false;
		InvokeRepeating(nameof(StartTimer), _iGameFrameLength * 0.001f, _iGameFrameLength * 0.001f);
	}

	private void StartTimer()
	{
		sm_iTotalGameFrameCount++;
	}

	/// <summary>
	/// 开启帧锁定.
	/// </summary>
	public void StartLockStep()
	{
		_bBeginLockStep = true;
//		sm_iValidGameFrameCount = 0;
//		sm_iTotalGameFrameCount = 0;
//		_iAccumilatedTime = 0;
	}

	/// <summary>
	/// 设置游戏逻辑帧率.
	/// </summary>
	/// <param name="gameFramesPerSecond">每秒帧数.</param>
	public void SetGameFrameRate(int gameFramesPerSecond)
	{
		_iGameFramesPerSecond = gameFramesPerSecond;
		_iGameFrameLength = Convert.ToInt32(1.0f / (float)gameFramesPerSecond * 1000);
		_iGameFramesPerLockStepTurn = INITIAL_LOCKSTEP_LENGTH / _iGameFrameLength;
		if (INITIAL_LOCKSTEP_LENGTH % _iGameFrameLength > 0)
		{
			_iGameFramesPerLockStepTurn++;
		}
	}

	/// <summary>
	/// 执行游戏逻辑帧.
	/// </summary>
	private void GameFrameRun()
	{
		if (_iGameFrameInLockStepTurn == 0)
		{
			if (!LockStepTurn())
			{
				UnityEngine.Debug.Log(string.Format("等待锁定帧{0}的消息包，游戏暂停！游戏逻辑帧{1}。", _iLockStepTurnID, GameDriver.sm_iValidGameFrameCount));
//				Utils.GamePause(true);
				return;
			}
		}

//		GameTurnSW.Start();

		sm_iValidGameFrameCount++;
		_bCurrentServerTurnPackageSended = false;

		//关卡AI刷新
		// TollgateAI.Instance.DoUpdate();
		//游戏世界刷新
		// GameWorld.Instance.DoUpdate();
		//弹药刷新
		// AmmoManager.Instance.DoUpdate();
		//地面BUFF刷新
		// TerrainBufferMgr.Instance.DoUpdate();
		//角色Buff刷新
		// RoleBuffManager.Instance.DoUpdate();
		//怪物生成器刷新
		// MonsterWaveCreator.Instance.DoUpdate();
		//计时器刷新
		// vp_Timer.Instance.DoUpdate();
		//寻路
		// AIGridPathFind.Instance.DoUpdate();

		_iGameFrameInLockStepTurn++;
		if (_iGameFrameInLockStepTurn == _iGameFramesPerLockStepTurn)
		{
			_iGameFrameInLockStepTurn = 0;
		}

//		GameTurnSW.Stop();
//		long runtime = Convert.ToInt32 ((Time.deltaTime * 1000)) + GameTurnSW.ElapsedMilliseconds;
//		if(runtime > _lCurrentGameFrameRuntime)
//		{
//			_lCurrentGameFrameRuntime = runtime;
//		}
//		GameTurnSW.Reset();
	}

	/// <summary>
	/// 执行进入下一个锁定帧逻辑.
	/// </summary>
	/// <returns>是否执行成功.</returns>
	private bool LockStepTurn()
	{
		bool nextTurn = NextTurn();
		if (nextTurn) 
		{
			_priorNetSW = CurrentNetSW;
			CurrentNetSW.Reset();
			CurrentNetSW.Start();

			//发送这个锁定帧的指令
			if (_iLockStepTurnID > 0)
			{
				// GameNetManager.Instance.SendTurnPackageToServer();
			}
			if (_iLockStepTurnID > 2) 
			{
				//执行本次锁定帧的指令
				// GameNetManager.Instance.ProcessActions();
			}

			//下一个锁定帧
			_iLockStepTurnID++;
		}
		
		UpdateGameFrameRate();
		return nextTurn;
	}

	/// <summary>
	/// 是否可以进入下一轮锁定帧.
	/// </summary>
	private bool NextTurn()
	{
		//第一次和第二次锁定帧没有消息需要处理
		if (_iLockStepTurnID <= 2)
		{
			return true;
		}
		//收到上锁定帧消息则可以继续下一轮
		// if (GameNetManager.Instance.ReceivedTurnIDList.Contains(_iLockStepTurnID))
		{
			return true;
		}

//		if (GameNetManager.Instance.ReceivedTurnIDList.Count > 0)
//		{
//			return true;
//		}

		return false;
	}

	private void UpdateGameFrameRate() 
	{

	}
}
