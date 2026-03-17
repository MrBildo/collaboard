import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Loader2, Search, X } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { searchAllCards } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { cn } from '@/lib/utils';
import type { SearchResult } from '@/types';

export function SearchCommand() {
  const navigate = useNavigate();
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [dismissedQuery, setDismissedQuery] = useState<string | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // Debounce the query by 300ms
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedQuery(query.trim());
    }, 300);
    return () => clearTimeout(timer);
  }, [query]);

  // Derive open state: show dropdown when query is valid and not dismissed for this specific query
  const open = debouncedQuery.length >= 2 && dismissedQuery !== debouncedQuery;

  // Close on outside click
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setDismissedQuery(debouncedQuery);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Keyboard shortcuts: "/" or Ctrl+K to focus, Escape to close
  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === '/' && !isInputFocused()) {
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

  const handleSelect = (boardSlug: string, cardNumber: number) => {
    setDismissedQuery(debouncedQuery);
    setQuery('');
    setDebouncedQuery('');
    navigate(`/boards/${boardSlug}/cards/${cardNumber}`);
  };

  const handleClear = () => {
    setQuery('');
    setDebouncedQuery('');
    setDismissedQuery(debouncedQuery);
    inputRef.current?.focus();
  };

  const handleInputKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      setDismissedQuery(debouncedQuery);
      inputRef.current?.blur();
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
          <button
            type="button"
            onClick={handleClear}
            className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground"
          >
            <X className="w-4 h-4" />
          </button>
        )}
      </div>
      {open && (
        <div className="absolute top-full mt-1 w-full max-h-80 overflow-y-auto rounded-lg border border-border bg-popover shadow-md z-50">
          {searchQuery.isLoading && (
            <p className="px-3 py-4 text-sm text-muted-foreground">Searching...</p>
          )}
          {searchQuery.isSuccess && totalCards === 0 && (
            <p className="px-3 py-4 text-sm text-muted-foreground">No cards match your search</p>
          )}
          {searchQuery.isSuccess && totalCards > 0 && results.map((group) => (
            <div key={group.boardId}>
              {results.length > 1 && (
                <div className="sticky top-0 bg-muted px-3 py-1.5 text-xs font-medium text-muted-foreground">
                  {group.boardName}
                </div>
              )}
              {group.cards.map((card) => (
                <button
                  key={card.id}
                  type="button"
                  onClick={() => handleSelect(group.boardSlug, card.number)}
                  className={cn(
                    'flex w-full items-start gap-2 px-3 py-2 text-left text-sm',
                    'hover:bg-accent/10 focus-visible:bg-accent/10 focus-visible:outline-none',
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
              ))}
            </div>
          ))}
          {searchQuery.isError && (
            <p className="px-3 py-4 text-sm text-destructive">Search failed. Please try again.</p>
          )}
        </div>
      )}
    </div>
  );
}

function isInputFocused(): boolean {
  const tag = document.activeElement?.tagName?.toLowerCase();
  return tag === 'input' || tag === 'textarea' || tag === 'select';
}
