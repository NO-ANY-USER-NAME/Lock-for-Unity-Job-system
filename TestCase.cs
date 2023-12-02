using System.Threading;
using System.Runtime.CompilerServices;

using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

public sealed class TestCase : MonoBehaviour{
    private struct LockStruct<T>where T:unmanaged{
        private const int LOCKED=1,FREED=0;
        public T value;
        private int _lock;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void InitialLock(){//initial the _lock variable
            _lock = FREED;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AcquireLock(){
            while(Interlocked.CompareExchange(ref _lock,LOCKED,FREED)!=FREED){}//keep spinning until the _lock is FREED
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReleaseLock(){
            Interlocked.Exchange(ref _lock, FREED);//release the lock
        }
    }

    [BurstCompile]
    unsafe private struct Run:IJob{
        [NativeDisableUnsafePtrRestriction]public LockStruct<int>* test;
        [NativeDisableUnsafePtrRestriction]public int2* writtenResult;
        public int length;
        public Unity.Mathematics.Random rand;
        public void Execute(){
            int idx=rand.NextInt(0,length);
            int val=rand.NextInt(100,200);
            writtenResult->x=idx;
            writtenResult->y=val;

            (test+idx)->AcquireLock();
            (test+idx)->value+=val;
            (test+idx)->ReleaseLock();
        }
    }

    unsafe public void Awake(){
        const int Length=8,JobNumber=2048;

        UnsafeList<LockStruct<int>> lists=new UnsafeList<LockStruct<int>>(Length,Allocator.TempJob){Length=Length};
        UnsafeList<int2> writtenResults = new UnsafeList<int2>(JobNumber, Allocator.TempJob){Length=JobNumber};
        NativeArray<JobHandle> handles=new NativeArray<JobHandle>(JobNumber,Allocator.TempJob);

        for(int i=0;i<Length;i++){
            (lists.Ptr+i)->InitialLock();
            (lists.Ptr+i)->value=0;
        }

        for(int i=0;i<JobNumber;i++){
            handles[i]=new Run{
                test=lists.Ptr,
                writtenResult=writtenResults.Ptr+i,
                length=Length,
                rand=new Unity.Mathematics.Random(unchecked((uint)UnityEngine.Random.Range(int.MinValue,int.MaxValue))),
            }.Schedule();
        }
        JobHandle.CompleteAll(handles);

        int*expected=stackalloc int[Length];
        UnsafeUtility.MemSet(expected,0,Length<<2);
        for(int i=0;i<JobNumber;i++){
            expected[writtenResults[i].x]+=writtenResults[i].y;
        }

        for(int i=0;i<Length;i++){
            if(expected[i]!=lists[i].value){
                Debug.Log($"fail at {i} {lists[i].value} expected {expected[i]}");
            }
            else{
                Debug.Log("pass");
            }
        }

        lists.Dispose();
        writtenResults.Dispose();
        handles.Dispose();
    }
}
