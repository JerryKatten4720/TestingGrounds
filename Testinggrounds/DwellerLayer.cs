using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace IsometricWPF.Dwellers
{
    public class DwellerLayer
    {
        private readonly List<DwellerInstance> _dwellers = new();
        private DwellerInstance _selectedDweller;
        private readonly IsometricRenderer _renderer;

        public event Action<DwellerInstance>           DwellerSelected;
        public event Action<DwellerInstance, int, int> DwellerMoved;

        public IReadOnlyList<DwellerInstance> Dwellers => _dwellers;
        public DwellerInstance Selected => _selectedDweller;

        public DwellerLayer(IsometricRenderer renderer) => _renderer = renderer;

        public void Add(DwellerInstance dweller)
        {
            _dwellers.Add(dweller);
            _renderer.LoadDwellers(_dwellers);
        }

        public void Remove(DwellerInstance dweller)
        {
            _dwellers.Remove(dweller);
            if (_selectedDweller == dweller) _selectedDweller = null;
            _renderer.LoadDwellers(_dwellers);
        }

        public void ClearAll()
        {
            _dwellers.Clear();
            _selectedDweller = null;
            _renderer.LoadDwellers(_dwellers);
        }

        public bool IsValidMove(int gridX, int gridY)
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var map = mainWindow?.World;
            if (map == null || !map.IsInBounds(gridX, gridY)) return false;

            var cell = map[gridX, gridY];
            

            if (cell.Blocks.Count == 0) return false;


            int maxHeight = -1;
            foreach (var h in cell.Blocks.Keys) if (h > maxHeight) maxHeight = h;
            
            if (maxHeight < 0) return false;
            
            string topBlockName = cell.Blocks[maxHeight];
            bool isWalkable = cell.IsWalkableOverride ?? TileRegistry.Get(topBlockName).IsWalkable;
            if (!isWalkable) return false;


            bool isOccupied = _dwellers.Any(dw => dw != _selectedDweller && dw.TileX == gridX && dw.TileY == gridY);
            return !isOccupied;
        }

        public bool HandleTileClick(int gridX, int gridY, bool isEditorMode)
        {
            if (_selectedDweller != null)
            {
                if (IsValidMove(gridX, gridY))
                {
                    MoveDweller(_selectedDweller, gridX, gridY);
                    Deselect();
                    return true;
                }
            }

            var hitDweller = _dwellers.FirstOrDefault(dw => dw.TileX == gridX && dw.TileY == gridY);
            if (hitDweller != null)
            {
                Select(hitDweller);
                return true;
            }

            Deselect();
            return false;
        }

        public void Select(DwellerInstance dweller)
        {
            if (_selectedDweller != null) _selectedDweller.State = DwellerState.Idle;
            _selectedDweller = dweller;
            dweller.State = DwellerState.Selected;
            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
            DwellerSelected?.Invoke(dweller);
        }

        public void Deselect()
        {
            if (_selectedDweller == null) return;
            _selectedDweller.State = DwellerState.Idle;
            _selectedDweller = null;
            DwellerVisualFactory.InvalidateCache();
            _renderer.Redraw();
        }

        public void MoveDweller(DwellerInstance dweller, int newX, int newY)
        {
            dweller.TileX = newX;
            dweller.TileY = newY;
            _renderer.Redraw();
            DwellerMoved?.Invoke(dweller, newX, newY);
        }

        public void RefreshPositions() => _renderer.Redraw();
    }
}
