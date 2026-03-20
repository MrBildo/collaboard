import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { LayoutDashboard, Loader2, Search, X } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { searchAllCards } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { cn, isTextInputFocused } from '@/lib/utils';
import { useDebounce } from '@/hooks/use-debounce';
import { useClickOutside } from '@/hooks/use-click-outside';
import type { SearchResult } from '@/types';

export function SearchCommand() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [dismissedQuery, setDismissedQuery] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const debouncedQuery = useDebounce(query.trim(), 300);

  // Derive open state: show dropdown when query is valid and not dismissed for this specific query
  const open = debouncedQuery.length >= 2 && dismissedQuery !== debouncedQuery;

  // Close on outside click — callback always sees latest debouncedQuery via latest-ref pattern in hook
  useClickOutside(containerRef, useCallback(() => {
    setDismissedQuery(debouncedQuery);
  }, [debouncedQuery]));

  // Keyboard shortcuts: "/" or Ctrl+K to focus
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (document.querySelector('[data-slot="dialog-overlay"]')) return;
      if (event.key === '/' && !isTextInputFocused()) {
        event.preventDefault();
        inputRef.current?.focus();
      }
      if ((event.ctrlKey || event.metaKey) && event.key === 'k') {
        event.preventDefault();
        inputRef.current?.focus();
      }
    }
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, []);

  const searchQuery = useQuery({
    queryKey: queryKeys.search.cards(debouncedQuery),
    queryFn: () => searchAllCards(debouncedQuery),
    enabled: debouncedQuery.length >= 2,
    staleTime: 30_000,
  });

  const results: SearchResult[] = searchQuery.data ?? [];
  const totalCards = results.reduce((sum, r) => sum + r.cards.length, 0);

  const flatResults = useMemo(
    () => results.flatMap((group) => group.cards.map((card) => ({ boardSlug: group.boardSlug, card }))),
    [results],
  );

  const [focusedIndex, setFocusedIndex] = useState(-1);
  const dropdownRef = useRef<HTMLDivElement>(null);

  // Reset focused index when results change
  useEffect(() => { setFocusedIndex(-1); }, [flatResults.length]);

  // Scroll focused item into view, accounting for sticky group headers
  useEffect(() => {
    const dropdown = dropdownRef.current;
    if (!dropdown) return;
    if (focusedIndex <= 0) {
      dropdown.scrollTop = 0;
      return;
    }
    const el = dropdown.querySelector<HTMLElement>(`[data-search-index="${focusedIndex}"]`);
    if (!el) return;

    const dropdownRect = dropdown.getBoundingClientRect();
    const elRect = el.getBoundingClientRect();
    const headerOffset = 28; // sticky group header height (~py-1.5 + text-xs + px-3)

    if (elRect.top < dropdownRect.top + headerOffset) {
      dropdown.scrollTop -= (dropdownRect.top + headerOffset) - elRect.top;
    } else if (elRect.bottom > dropdownRect.bottom) {
      dropdown.scrollTop += elRect.bottom - dropdownRect.bottom;
    }
  }, [focusedIndex]);

  const handleSelect = (boardSlug: string, cardNumber: number) => {
    setDismissedQuery(debouncedQuery);
    setQuery('');
    setFocusedIndex(-1);
    navigate(`/boards/${boardSlug}/cards/${cardNumber}`);
  };

  const handleClear = () => {
    setQuery('');
    setDismissedQuery(debouncedQuery);
    setFocusedIndex(-1);
    inputRef.current?.focus();
  };

  const handleInputKeyDown = (event: React.KeyboardEvent) => {
    console.log('[SearchCommand] keydown:', event.key, { open, flatResultsLen: flatResults.length, focusedIndex });
    if (event.key === 'Escape') {
      setDismissedQuery(debouncedQuery);
      setFocusedIndex(-1);
      inputRef.current?.blur();
      return;
    }
    if (!open || flatResults.length === 0) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      setFocusedIndex((prev) => { console.log('[SearchCommand] ArrowDown:', prev, '->', Math.min(prev + 1, flatResults.length - 1)); return Math.min(prev + 1, flatResults.length - 1); });
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      setFocusedIndex((prev) => { console.log('[SearchCommand] ArrowUp:', prev, '->', Math.max(prev - 1, 0)); return Math.max(prev - 1, 0); });
    } else if (event.key === 'Enter' && focusedIndex >= 0) {
      event.preventDefault();
      const item = flatResults[focusedIndex];
      handleSelect(item.boardSlug, item.card.number);
    }
  };

  return (
    <div ref={containerRef} className="relative w-full max-w-md">
      <div className="relative">
        {searchQuery.isFetching ? (
          <Loader2 className="pointer-events-none absolute left-2.5 top-1/2 w-4 h-4 -translate-y-1/2 animate-spin text-muted-foreground" />
        ) : (
          <Search className="pointer-events-none absolute left-2.5 top-1/2 w-4 h-4 -translate-y-1/2 text-muted-foreground" />
        )}
        <Input
          ref={inputRef}
          type="text"
          placeholder="Search cards... (/ or Ctrl+K)"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onFocus={() => { if (debouncedQuery.length >= 2) setDismissedQuery(null); }}
          onKeyDown={handleInputKeyDown}
          className="pl-8 pr-8"
        />
        {query.length > 0 && (
          <Button
            variant="ghost"
            size="icon-xs"
            onClick={handleClear}
            className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
          >
            <X className="w-4 h-4" />
          </Button>
        )}
      </div>
      {open && (
        <div ref={dropdownRef} className="absolute top-full mt-1 w-full max-h-80 overflow-y-auto rounded-lg border border-border bg-popover shadow-md z-50">
          {searchQuery.isLoading && (
            <p className="px-3 py-4 text-sm text-muted-foreground">Searching...</p>
          )}
          {searchQuery.isSuccess && totalCards === 0 && (
            <p className="px-3 py-4 text-sm text-muted-foreground">No cards match your search</p>
          )}
          {searchQuery.isSuccess && totalCards > 0 && (() => {
            let flatIndex = 0;
            return results.map((group) => (
              <div key={group.boardId}>
                {results.length > 1 && (
                  <div className="sticky top-0 flex items-center gap-1.5 border-b border-border bg-muted px-3 py-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                    <LayoutDashboard className="h-3 w-3" />
                    {group.boardName}
                  </div>
                )}
                {group.cards.map((card) => {
                  const idx = flatIndex++;
                  return (
                    <button
                      key={card.id}
                      type="button"
                      data-search-index={idx}
                      onClick={() => handleSelect(group.boardSlug, card.number)}
                      onMouseEnter={() => setFocusedIndex(idx)}
                      className={cn(
                        'flex w-full items-start gap-2 px-3 py-2 text-left text-sm',
                        'hover:bg-accent/10 focus-visible:bg-accent/10 focus-visible:outline-none',
                        idx === focusedIndex && 'bg-accent text-accent-foreground',
                      )}
                    >
                      <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs font-mono text-muted-foreground">
                        #{card.number}
                      </span>
                      <div className="min-w-0 flex-1">
                        <p className="truncate font-medium text-foreground">{card.name}</p>
                        {card.descriptionMarkdown && (
                          <p className="mt-0.5 truncate text-xs text-muted-foreground">
                            {card.descriptionMarkdown.slice(0, 100)}
                          </p>
                        )}
                      </div>
                    </button>
                  );
                })}
              </div>
            ));
          })()}
          {searchQuery.isError && (
            <p className="px-3 py-4 text-sm text-destructive">Search failed. Please try again.</p>
          )}
        </div>
      )}
    </div>
  );
}
