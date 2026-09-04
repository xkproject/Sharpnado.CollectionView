using System;
using System.Windows.Input;

using Android.Runtime;

using AndroidX.RecyclerView.Widget;

using Sharpnado.CollectionView.RenderedViews;
using Sharpnado.CollectionView.ViewModels;

using Xamarin.Forms;

namespace Sharpnado.CollectionView.Droid.Renderers
{
    public partial class CollectionViewRenderer
    {
        private class DragAnDropItemTouchHelperCallback : ItemTouchHelper.Callback
        {
            private readonly CollectionView.RenderedViews.CollectionView _collection;

            private readonly RecycleViewAdapter _recycleViewAdapter;
            private readonly ICommand _onDragAndDropdEnded;
            private readonly ICommand _onDragAndDropStart;

            private int _from = -1;
            private int _to = -1;

            private DraggableViewCell _draggedViewCell;

            private bool _isRefreshViewUserEnabled = false;

            /// <summary>
            /// Speed of the auto scroll triggered by dragging an item past the edge of the collection, in
            /// density independent pixels so it covers the same amount of content on every screen. It is
            /// the speed the previous fixed step of 30 pixels per call produced on a 60 Hz screen of
            /// density 2.
            /// </summary>
            private const double OutOfBoundsScrollDipsPerSecond = 900d;

            private const long AssumedFrameMilliseconds = 16;

            private long _lastOutOfBoundsScrollMilliseconds;

            public DragAnDropItemTouchHelperCallback(IntPtr handle, JniHandleOwnership transfer)
                : base(handle, transfer)
            {
            }

            public DragAnDropItemTouchHelperCallback(CollectionView.RenderedViews.CollectionView collection, RecycleViewAdapter recycleViewAdapter, ICommand onDragAndDropStart = null, ICommand onDragAndDropdEnded = null)
            {
                _collection = collection;
                _recycleViewAdapter = recycleViewAdapter;
                _onDragAndDropStart = onDragAndDropStart;
                _onDragAndDropdEnded = onDragAndDropdEnded;
            }

            public override bool IsLongPressDragEnabled => _collection.DragAndDropTrigger == DragAndDropTrigger.LongTap;

            public override int GetMovementFlags(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
            {
                if (((ViewHolder)viewHolder).ViewCell is DraggableViewCell draggableViewCell
                    && !draggableViewCell.IsDraggable)
                {
                    return 0;
                }

                switch (_collection.DragAndDropDirection)
                {
                    case DragAndDropDirection.VerticalOnly:
                        return MakeMovementFlags(ItemTouchHelper.Up | ItemTouchHelper.Down, 0);
                    case DragAndDropDirection.HorizontalOnly:
                        return MakeMovementFlags(ItemTouchHelper.Left | ItemTouchHelper.Right, 0);
                }

                return MakeMovementFlags(ItemTouchHelper.Left | ItemTouchHelper.Right | ItemTouchHelper.Up | ItemTouchHelper.Down, 0);
            }

            public override void OnSelectedChanged(RecyclerView.ViewHolder viewHolder, int actionState)
            {
                base.OnSelectedChanged(viewHolder, actionState);

                if (actionState == ItemTouchHelper.ActionStateDrag)
                {
                    _lastOutOfBoundsScrollMilliseconds = 0;
                    _collection.IsDragAndDropping = true;
                    if (_collection.IsInPullToRefresh() && _collection.Parent is ContentView refreshView)
                    {
                        _isRefreshViewUserEnabled = refreshView.IsEnabled;
                        refreshView.IsEnabled = false;
                    }

                    if (((ViewHolder)viewHolder).ViewCell is DraggableViewCell draggableViewCell)
                    {
                        // System.Diagnostics.Debug.WriteLine($">>>>> OnSelectedChanged( {draggableViewCell.BindingContext} IsDragAndDropping: true )");
                        draggableViewCell.IsDragAndDropping = true;
                        _draggedViewCell = draggableViewCell;
                    }

                    _onDragAndDropStart?.Execute(new DragAndDropInfo(
                        viewHolder.AdapterPosition,
                        -1,
                        ((ViewHolder)viewHolder).BindingContext));
                }
                else if (actionState == ItemTouchHelper.ActionStateIdle)
                {
                    _collection.IsDragAndDropping = false;
                    if (_collection.IsInPullToRefresh() && _collection.Parent is ContentView refreshView && _isRefreshViewUserEnabled)
                    {
                        refreshView.IsEnabled = true;
                    }

                    if (_draggedViewCell != null)
                    {
                        // System.Diagnostics.Debug.WriteLine($">>>>> OnSelectedChanged( {_draggedViewCell.BindingContext} IsDragAndDropping: false )");
                        _draggedViewCell.IsDragAndDropping = false;
                        _draggedViewCell = null;
                    }
                }
            }

            public override bool OnMove(
                RecyclerView recyclerView,
                RecyclerView.ViewHolder viewHolder,
                RecyclerView.ViewHolder target)
            {
                if (((ViewHolder)target).ViewCell is DraggableViewCell draggableViewCell
                    && !draggableViewCell.IsDraggable)
                {
                    return false;
                }

                if (_from == -1)
                {
                    _from = viewHolder.AdapterPosition;
                }

                _to = target.AdapterPosition;

                // System.Diagnostics.Debug.WriteLine($">>>>> OnMove( from: {_from}, to: {_to} )");
                _recycleViewAdapter.OnItemMoving(viewHolder.AdapterPosition, target.AdapterPosition);

                return true;
            }

            public override void OnMoved(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder, int fromPos, RecyclerView.ViewHolder target, int toPos, int x, int y)
            {
                base.OnMoved(recyclerView, viewHolder, fromPos, target, toPos, x, y);

                // recompute items offsets
                recyclerView.InvalidateItemDecorations();
            }

            /// <summary>
            /// Distance to auto scroll on each call. It is derived from the time elapsed since the previous
            /// call, not fixed per call, because this runs once per animation frame: a fixed step scrolls
            /// as many times faster as the screen is quicker, so a 90 Hz tablet ran away compared with a
            /// 60 Hz phone. It is also scaled by the screen density, so a less dense screen does not cover
            /// more content per second.
            /// </summary>
            public override int InterpolateOutOfBoundsScroll(
                RecyclerView recyclerView,
                int viewSize,
                int viewSizeOutOfBounds,
                int totalSize,
                long msSinceStartScroll)
            {
                long elapsedMilliseconds = msSinceStartScroll - _lastOutOfBoundsScrollMilliseconds;
                _lastOutOfBoundsScrollMilliseconds = msSinceStartScroll;
                if (elapsedMilliseconds <= 0 || elapsedMilliseconds > 100)
                {
                    elapsedMilliseconds = AssumedFrameMilliseconds;
                }

                double density = recyclerView.Context?.Resources?.DisplayMetrics?.Density ?? 1d;
                int pixels = (int)Math.Round(OutOfBoundsScrollDipsPerSecond * density * elapsedMilliseconds / 1000d);
                return Math.Sign(viewSizeOutOfBounds) * Math.Max(1, pixels);
            }

            public override float GetMoveThreshold(RecyclerView.ViewHolder viewHolder)
            {
                float result = 1f;
                return result;
            }

            public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
            {
                base.ClearView(recyclerView, viewHolder);

                if (_from > -1 && _to > -1)
                {
                    _recycleViewAdapter.OnItemMovedFromDragAndDrop(_from, _to);
                    _onDragAndDropdEnded?.Execute(new DragAndDropInfo(
                        _from,
                        _to,
                        ((ViewHolder)viewHolder).BindingContext));
                    _from = _to = -1;
                }
            }

            public override void OnSwiped(RecyclerView.ViewHolder viewHolder, int direction)
            {
                throw new NotSupportedException();
            }
        }
    }
}