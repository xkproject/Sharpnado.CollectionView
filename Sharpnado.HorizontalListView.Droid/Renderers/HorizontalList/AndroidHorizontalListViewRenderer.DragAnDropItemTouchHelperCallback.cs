﻿using System;
using System.Windows.Input;

using Android.Runtime;

using Sharpnado.HorizontalListView.RenderedViews;
using Sharpnado.HorizontalListView.ViewModels;

using Xamarin.Forms;
#if __ANDROID_29__
using AndroidX.RecyclerView.Widget;
#else
using Android.Support.V7.Widget;
using Android.Support.V7.Widget.Helper;
#endif

namespace Sharpnado.HorizontalListView.Droid.Renderers.HorizontalList
{
    public partial class AndroidHorizontalListViewRenderer
    {
        private class DragAnDropItemTouchHelperCallback : ItemTouchHelper.Callback
        {
            private readonly HorizontalListView.RenderedViews.HorizontalListView _horizontalList;

            private readonly RecycleViewAdapter _recycleViewAdapter;
            private readonly ICommand _onDragAndDropdEnded;
            private readonly ICommand _onDragAndDropStart;

            private int _from = -1;
            private int _to = -1;

            private DraggableViewCell _draggedViewCell;

            private bool _isRefreshViewUserEnabled = false;

            public DragAnDropItemTouchHelperCallback(IntPtr handle, JniHandleOwnership transfer)
                : base(handle, transfer)
            {
            }

            public DragAnDropItemTouchHelperCallback(HorizontalListView.RenderedViews.HorizontalListView horizontalList, RecycleViewAdapter recycleViewAdapter, ICommand onDragAndDropStart = null, ICommand onDragAndDropdEnded = null)
            {
                _horizontalList = horizontalList;
                _recycleViewAdapter = recycleViewAdapter;
                _onDragAndDropStart = onDragAndDropStart;
                _onDragAndDropdEnded = onDragAndDropdEnded;
            }

            public override bool IsLongPressDragEnabled => _horizontalList.DragAndDropTrigger == DragAndDropTrigger.LongTap;

            public override int GetMovementFlags(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
            {
                if (((ViewHolder)viewHolder).ViewCell is DraggableViewCell draggableViewCell
                    && !draggableViewCell.IsDraggable)
                {
                    return 0;
                }

                switch (_horizontalList.DragAndDropDirection)
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
                    _horizontalList.IsDragAndDropping = true;
                    if (_horizontalList.IsInPullToRefresh() && _horizontalList.Parent is ContentView refreshView)
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
                    _horizontalList.IsDragAndDropping = false;
                    if (_horizontalList.IsInPullToRefresh() && _horizontalList.Parent is ContentView refreshView && _isRefreshViewUserEnabled)
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

            public override int InterpolateOutOfBoundsScroll(
                RecyclerView recyclerView,
                int viewSize,
                int viewSizeOutOfBounds,
                int totalSize,
                long msSinceStartScroll)
            {
                int result = Math.Sign(viewSizeOutOfBounds) * 30;
                return result;
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