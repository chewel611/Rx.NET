﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace System.Linq
{
    public static partial class AsyncEnumerable
    {
        public static IAsyncEnumerable<TSource> Concat<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return Create(() =>
            {
                var switched = false;
                var e = first.GetEnumerator();

                var cts = new CancellationTokenDisposable();
                var a = new AssignableDisposable { Disposable = e };
                var d = Disposable.Create(cts, a);

                var f = default(Action<TaskCompletionSource<bool>, CancellationToken>);
                f = (tcs, ct) => e.MoveNext(ct).Then(t =>
                {
                    t.Handle(tcs, res =>
                    {
                        if (res)
                        {
                            tcs.TrySetResult(true);
                        }
                        else
                        {
                            if (switched)
                            {
                                tcs.TrySetResult(false);
                            }
                            else
                            {
                                switched = true;

                                e = second.GetEnumerator();
                                a.Disposable = e;

                                f(tcs, ct);
                            }
                        }
                    });
                });

                return Create(
                    (ct, tcs) =>
                    {
                        f(tcs, cts.Token);
                        return tcs.Task.UsingEnumerator(a);
                    },
                    () => e.Current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TResult> Zip<TFirst, TSecond, TResult>(this IAsyncEnumerable<TFirst> first, IAsyncEnumerable<TSecond> second, Func<TFirst, TSecond, TResult> selector)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");
            if (selector == null)
                throw new ArgumentNullException("selector");

            return Create(() =>
            {
                var e1 = first.GetEnumerator();
                var e2 = second.GetEnumerator();
                var current = default(TResult);

                var cts = new CancellationTokenDisposable();
                var d = Disposable.Create(cts, e1, e2);

                return Create(
                    (ct, tcs) =>
                    {
                        e1.MoveNext(cts.Token).Zip(e2.MoveNext(cts.Token), (f, s) =>
                        {
                            var result = f && s;
                            if (result)
                                current = selector(e1.Current, e2.Current);
                            return result;
                        }).Then(t =>
                        {
                            t.Handle(tcs, x => tcs.TrySetResult(x));
                        });

                        return tcs.Task.UsingEnumerator(e1).UsingEnumerator(e2);
                    },
                    () => current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TSource> Except<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");
            if (comparer == null)
                throw new ArgumentNullException("comparer");

            return Create(() =>
            {
                var e = first.GetEnumerator();

                var cts = new CancellationTokenDisposable();
                var d = Disposable.Create(cts, e);

                var mapTask = default(Task<Dictionary<TSource, TSource>>);
                var getMapTask = new Func<CancellationToken, Task<Dictionary<TSource, TSource>>>(ct =>
                {
                    if (mapTask == null)
                        mapTask = second.ToDictionary(x => x, comparer, ct);
                    return mapTask;
                });

                var f = default(Action<TaskCompletionSource<bool>, CancellationToken>);
                f = (tcs, ct) =>
                {
                    e.MoveNext(ct).Zip(getMapTask(ct), (b, _) => b).Then(t =>
                    {
                        t.Handle(tcs, res =>
                        {
                            if (res)
                            {
                                if (!mapTask.Result.ContainsKey(e.Current))
                                    tcs.TrySetResult(true);
                                else
                                    f(tcs, ct);
                            }
                            else
                                tcs.TrySetResult(false);
                        });
                    });
                };

                return Create(
                    (ct, tcs) =>
                    {
                        f(tcs, cts.Token);
                        return tcs.Task.UsingEnumerator(e);
                    },
                    () => e.Current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TSource> Except<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return first.Except(second, EqualityComparer<TSource>.Default);
        }

        public static IAsyncEnumerable<TSource> Intersect<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");
            if (comparer == null)
                throw new ArgumentNullException("comparer");

            return Create(() =>
            {
                var e = first.GetEnumerator();

                var cts = new CancellationTokenDisposable();
                var d = Disposable.Create(cts, e);

                var mapTask = default(Task<Dictionary<TSource, TSource>>);
                var getMapTask = new Func<CancellationToken, Task<Dictionary<TSource, TSource>>>(ct =>
                {
                    if (mapTask == null)
                        mapTask = second.ToDictionary(x => x, comparer, ct);
                    return mapTask;
                });

                var f = default(Action<TaskCompletionSource<bool>, CancellationToken>);
                f = (tcs, ct) =>
                {
                    e.MoveNext(ct).Zip(getMapTask(ct), (b, _) => b).Then(t =>
                    {
                        t.Handle(tcs, res =>
                        {
                            if (res)
                            {
                                if (mapTask.Result.ContainsKey(e.Current))
                                    tcs.TrySetResult(true);
                                else
                                    f(tcs, ct);
                            }
                            else
                                tcs.TrySetResult(false);
                        });
                    });
                };

                return Create(
                    (ct, tcs) =>
                    {
                        f(tcs, cts.Token);
                        return tcs.Task.UsingEnumerator(e);
                    },
                    () => e.Current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TSource> Intersect<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return first.Intersect(second, EqualityComparer<TSource>.Default);
        }

        public static IAsyncEnumerable<TSource> Union<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second, IEqualityComparer<TSource> comparer)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");
            if (comparer == null)
                throw new ArgumentNullException("comparer");

            return first.Concat(second).Distinct(comparer);
        }

        public static IAsyncEnumerable<TSource> Union<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return first.Union(second, EqualityComparer<TSource>.Default);
        }

        public static Task<bool> SequenceEqual<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second, IEqualityComparer<TSource> comparer, CancellationToken cancellationToken)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");
            if (comparer == null)
                throw new ArgumentNullException("comparer");

            var tcs = new TaskCompletionSource<bool>();

            var e1 = first.GetEnumerator();
            var e2 = second.GetEnumerator();

            var run = default(Action<CancellationToken>);
            run = ct =>
            {
                e1.MoveNext(ct).Zip(e2.MoveNext(ct), (f, s) =>
                {
                    if (f ^ s)
                    {
                        tcs.TrySetResult(false);
                        return false;
                    }

                    if (f && s)
                    {
                        var eq = default(bool);
                        try
                        {
                            eq = comparer.Equals(e1.Current, e2.Current);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                            return false;
                        }

                        if (!eq)
                        {
                            tcs.TrySetResult(false);
                            return false;
                        }
                        else
                            return true;
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                        return false;
                    }
                }).Then(t =>
                {
                    t.Handle(tcs, res =>
                    {
                        if (res)
                            run(ct);
                    });
                });
            };

            run(cancellationToken);

            return tcs.Task.Finally(() =>
            {
                e1.Dispose();
                e2.Dispose();
            });
        }

        public static Task<bool> SequenceEqual<TSource>(this IAsyncEnumerable<TSource> first, IAsyncEnumerable<TSource> second, CancellationToken cancellationToken)
        {
            if (first == null)
                throw new ArgumentNullException("first");
            if (second == null)
                throw new ArgumentNullException("second");

            return first.SequenceEqual(second, EqualityComparer<TSource>.Default, cancellationToken);
        }

        public static IAsyncEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(this IAsyncEnumerable<TOuter> outer, IAsyncEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, IAsyncEnumerable<TInner>, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            if (outer == null)
                throw new ArgumentNullException("outer");
            if (inner == null)
                throw new ArgumentNullException("inner");
            if (outerKeySelector == null)
                throw new ArgumentNullException("outerKeySelector");
            if (innerKeySelector == null)
                throw new ArgumentNullException("innerKeySelector");
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");
            if (comparer == null)
                throw new ArgumentNullException("comparer");


            return new GroupJoinAsyncEnumerable<TOuter, TInner, TKey, TResult>(outer, inner, outerKeySelector, innerKeySelector, resultSelector, comparer);
        }

        public static IAsyncEnumerable<TResult> GroupJoin<TOuter, TInner, TKey, TResult>(this IAsyncEnumerable<TOuter> outer, IAsyncEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, IAsyncEnumerable<TInner>, TResult> resultSelector)
        {
            if (outer == null)
                throw new ArgumentNullException("outer");
            if (inner == null)
                throw new ArgumentNullException("inner");
            if (outerKeySelector == null)
                throw new ArgumentNullException("outerKeySelector");
            if (innerKeySelector == null)
                throw new ArgumentNullException("innerKeySelector");
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");

            return outer.GroupJoin(inner, outerKeySelector, innerKeySelector, resultSelector, EqualityComparer<TKey>.Default);
        }


        private sealed class GroupJoinAsyncEnumerable<TOuter, TInner, TKey, TResult> : IAsyncEnumerable<TResult>
        {
            private readonly IAsyncEnumerable<TOuter> _outer;
            private readonly IAsyncEnumerable<TInner> _inner;
            private readonly Func<TOuter, TKey> _outerKeySelector;
            private readonly Func<TInner, TKey> _innerKeySelector;
            private readonly Func<TOuter, IAsyncEnumerable<TInner>, TResult> _resultSelector;
            private readonly IEqualityComparer<TKey> _comparer;

            public GroupJoinAsyncEnumerable(
                IAsyncEnumerable<TOuter> outer,
                IAsyncEnumerable<TInner> inner,
                Func<TOuter, TKey> outerKeySelector,
                Func<TInner, TKey> innerKeySelector,
                Func<TOuter, IAsyncEnumerable<TInner>, TResult> resultSelector,
                IEqualityComparer<TKey> comparer)
            {
                _outer = outer;
                _inner = inner;
                _outerKeySelector = outerKeySelector;
                _innerKeySelector = innerKeySelector;
                _resultSelector = resultSelector;
                _comparer = comparer;
            }

            public IAsyncEnumerator<TResult> GetEnumerator()
                => new GroupJoinAsyncEnumerator(
                    _outer.GetEnumerator(),
                    _inner.GetEnumerator(),
                    _outerKeySelector,
                    _innerKeySelector,
                    _resultSelector,
                    _comparer);

            private sealed class GroupJoinAsyncEnumerator : IAsyncEnumerator<TResult>
            {
                private readonly IAsyncEnumerator<TOuter> _outer;
                private readonly IAsyncEnumerator<TInner> _inner;
                private readonly Func<TOuter, TKey> _outerKeySelector;
                private readonly Func<TInner, TKey> _innerKeySelector;
                private readonly Func<TOuter, IAsyncEnumerable<TInner>, TResult> _resultSelector;
                private readonly IEqualityComparer<TKey> _comparer;

                private Dictionary<TKey, List<TInner>> _innerGroups;

                public GroupJoinAsyncEnumerator(
                    IAsyncEnumerator<TOuter> outer,
                    IAsyncEnumerator<TInner> inner,
                    Func<TOuter, TKey> outerKeySelector,
                    Func<TInner, TKey> innerKeySelector,
                    Func<TOuter, IAsyncEnumerable<TInner>, TResult> resultSelector,
                    IEqualityComparer<TKey> comparer)
                {
                    _outer = outer;
                    _inner = inner;
                    _outerKeySelector = outerKeySelector;
                    _innerKeySelector = innerKeySelector;
                    _resultSelector = resultSelector;
                    _comparer = comparer;
                }

                public async Task<bool> MoveNext(CancellationToken cancellationToken)
                {
                    List<TInner> group;

                    if (!await _outer.MoveNext(cancellationToken))
                    {
                        return false;
                    }

                    if (_innerGroups == null)
                    {
                        _innerGroups = new Dictionary<TKey, List<TInner>>();

                        while (await _inner.MoveNext(cancellationToken))
                        {
                            var inner = _inner.Current;
                            var innerKey = _innerKeySelector(inner);

                            if (innerKey != null)
                            {
                                if (!_innerGroups.TryGetValue(innerKey, out group))
                                {
                                    _innerGroups.Add(innerKey, group = new List<TInner>());
                                }

                                group.Add(inner);
                            }
                        }
                    }

                    var outer = _outer.Current;
                    var outerKey = _outerKeySelector(outer);

                    Current
                        = _resultSelector(
                            outer,
                            new AsyncEnumerableAdapter<TInner>(
                                outerKey != null
                                && _innerGroups.TryGetValue(outerKey, out group)
                                    ? (IEnumerable<TInner>)group
                                    : EmptyEnumerable<TInner>.Instance));

                    return true;
                }

                public TResult Current { get; private set; }

                public void Dispose()
                {
                    _inner.Dispose();
                    _outer.Dispose();
                }

                private sealed class EmptyEnumerable<TElement>
                {
                    public static readonly TElement[] Instance = new TElement[0];
                }
            }
        }

        private sealed class AsyncEnumerableAdapter<T> : IAsyncEnumerable<T>
        {
            private readonly IEnumerable<T> _source;

            public AsyncEnumerableAdapter(IEnumerable<T> source)
            {
                _source = source;
            }

            public IAsyncEnumerator<T> GetEnumerator()
                => new AsyncEnumeratorAdapter(_source.GetEnumerator());

            private sealed class AsyncEnumeratorAdapter : IAsyncEnumerator<T>
            {
                private readonly IEnumerator<T> _enumerator;

                public AsyncEnumeratorAdapter(IEnumerator<T> enumerator)
                {
                    _enumerator = enumerator;
                }

                public Task<bool> MoveNext(CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();

#if HAS_AWAIT
                    return Task.FromResult(_enumerator.MoveNext());
#else
                    return TaskEx.FromResult(_enumerator.MoveNext());
#endif
                }

                public T Current => _enumerator.Current;

                public void Dispose() => _enumerator.Dispose();
            }
        }

        public static IAsyncEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(this IAsyncEnumerable<TOuter> outer, IAsyncEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, TInner, TResult> resultSelector, IEqualityComparer<TKey> comparer)
        {
            if (outer == null)
                throw new ArgumentNullException("outer");
            if (inner == null)
                throw new ArgumentNullException("inner");
            if (outerKeySelector == null)
                throw new ArgumentNullException("outerKeySelector");
            if (innerKeySelector == null)
                throw new ArgumentNullException("innerKeySelector");
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");
            if (comparer == null)
                throw new ArgumentNullException("comparer");

            return Create(() =>
            {
                var oe = outer.GetEnumerator();
                var ie = inner.GetEnumerator();

                var cts = new CancellationTokenDisposable();
                var d = Disposable.Create(cts, oe, ie);

                var current = default(TResult);
                var useOuter = true;
                var outerMap = new Dictionary<TKey, List<TOuter>>(comparer);
                var innerMap = new Dictionary<TKey, List<TInner>>(comparer);
                var q = new Queue<TResult>();

                var gate = new object();

                var f = default(Action<TaskCompletionSource<bool>, CancellationToken>);
                f = (tcs, ct) =>
                {
                    if (q.Count > 0)
                    {
                        current = q.Dequeue();
                        tcs.TrySetResult(true);
                        return;
                    }

                    var b = useOuter;
                    if (ie == null && oe == null)
                    {
                        tcs.TrySetResult(false);
                        return;
                    }
                    else if (ie == null)
                        b = true;
                    else if (oe == null)
                        b = false;
                    useOuter = !useOuter;

                    var enqueue = new Func<TOuter, TInner, bool>((o, i) =>
                    {
                        var result = default(TResult);
                        try
                        {
                            result = resultSelector(o, i);
                        }
                        catch (Exception exception)
                        {
                            tcs.TrySetException(exception);
                            return false;
                        }

                        q.Enqueue(result);
                        return true;
                    });

                    if (b)
                        oe.MoveNext(ct).Then(t =>
                        {
                            t.Handle(tcs, res =>
                            {
                                if (res)
                                {
                                    var element = oe.Current;
                                    var key = default(TKey);

                                    try
                                    {
                                        key = outerKeySelector(element);
                                    }
                                    catch (Exception exception)
                                    {
                                        tcs.TrySetException(exception);
                                        return;
                                    }

                                    var outerList = default(List<TOuter>);
                                    if (!outerMap.TryGetValue(key, out outerList))
                                    {
                                        outerList = new List<TOuter>();
                                        outerMap.Add(key, outerList);
                                    }

                                    outerList.Add(element);

                                    var innerList = default(List<TInner>);
                                    if (!innerMap.TryGetValue(key, out innerList))
                                    {
                                        innerList = new List<TInner>();
                                        innerMap.Add(key, innerList);
                                    }

                                    foreach (var v in innerList)
                                    {
                                        if (!enqueue(element, v))
                                            return;
                                    }
                                    
                                    f(tcs, ct);
                                }
                                else
                                {
                                    oe.Dispose();
                                    oe = null;
                                    f(tcs, ct);
                                }
                            });
                        });
                    else
                        ie.MoveNext(ct).Then(t =>
                        {
                            t.Handle(tcs, res =>
                            {
                                if (res)
                                {
                                    var element = ie.Current;
                                    var key = default(TKey);

                                    try
                                    {
                                        key = innerKeySelector(element);
                                    }
                                    catch (Exception exception)
                                    {
                                        tcs.TrySetException(exception);
                                        return;
                                    }

                                    var innerList = default(List<TInner>);
                                    if (!innerMap.TryGetValue(key, out innerList))
                                    {
                                        innerList = new List<TInner>();
                                        innerMap.Add(key, innerList);
                                    }

                                    innerList.Add(element);

                                    var outerList = default(List<TOuter>);
                                    if (!outerMap.TryGetValue(key, out outerList))
                                    {
                                        outerList = new List<TOuter>();
                                        outerMap.Add(key, outerList);
                                    }

                                    foreach (var v in outerList)
                                    {
                                        if (!enqueue(v, element))
                                            return;
                                    }

                                    f(tcs, ct);
                                }
                                else
                                {
                                    ie.Dispose();
                                    ie = null;
                                    f(tcs, ct);
                                }
                            });
                        });
                };

                return Create(
                    (ct, tcs) =>
                    {
                        f(tcs, cts.Token);
                        return tcs.Task.UsingEnumerator(oe).UsingEnumerator(ie);
                    },
                    () => current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TResult> Join<TOuter, TInner, TKey, TResult>(this IAsyncEnumerable<TOuter> outer, IAsyncEnumerable<TInner> inner, Func<TOuter, TKey> outerKeySelector, Func<TInner, TKey> innerKeySelector, Func<TOuter, TInner, TResult> resultSelector)
        {
            if (outer == null)
                throw new ArgumentNullException("outer");
            if (inner == null)
                throw new ArgumentNullException("inner");
            if (outerKeySelector == null)
                throw new ArgumentNullException("outerKeySelector");
            if (innerKeySelector == null)
                throw new ArgumentNullException("innerKeySelector");
            if (resultSelector == null)
                throw new ArgumentNullException("resultSelector");

            return outer.Join(inner, outerKeySelector, innerKeySelector, resultSelector, EqualityComparer<TKey>.Default);
        }

        public static IAsyncEnumerable<TSource> Concat<TSource>(this IEnumerable<IAsyncEnumerable<TSource>> sources)
        {
            if (sources == null)
                throw new ArgumentNullException("sources");

            return sources.Concat_();
        }

        public static IAsyncEnumerable<TSource> Concat<TSource>(params IAsyncEnumerable<TSource>[] sources)
        {
            if (sources == null)
                throw new ArgumentNullException("sources");

            return sources.Concat_();
        }

        private static IAsyncEnumerable<TSource> Concat_<TSource>(this IEnumerable<IAsyncEnumerable<TSource>> sources)
        {
            return Create(() =>
            {
                var se = sources.GetEnumerator();
                var e = default(IAsyncEnumerator<TSource>);

                var cts = new CancellationTokenDisposable();
                var a = new AssignableDisposable();
                var d = Disposable.Create(cts, se, a);

                var f = default(Action<TaskCompletionSource<bool>, CancellationToken>);
                f = (tcs, ct) =>
                {
                    if (e == null)
                    {
                        var b = false;
                        try
                        {
                            b = se.MoveNext();
                            if (b)
                                e = se.Current.GetEnumerator();
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                            return;
                        }

                        if (!b)
                        {
                            tcs.TrySetResult(false);
                            return;
                        }

                        a.Disposable = e;
                    }

                    e.MoveNext(ct).Then(t =>
                    {
                        t.Handle(tcs, res =>
                        {
                            if (res)
                            {
                                tcs.TrySetResult(true);
                            }
                            else
                            {
                                e.Dispose();
                                e = null;

                                f(tcs, ct);
                            }
                        });
                    });
                };

                return Create(
                    (ct, tcs) =>
                    {
                        f(tcs, cts.Token);
                        return tcs.Task.UsingEnumerator(a);
                    },
                    () => e.Current,
                    d.Dispose
                );
            });
        }

        public static IAsyncEnumerable<TOther> SelectMany<TSource, TOther>(this IAsyncEnumerable<TSource> source, IAsyncEnumerable<TOther> other)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            if (other == null)
                throw new ArgumentNullException("other");

            return source.SelectMany(_ => other);
        }
    }
}
