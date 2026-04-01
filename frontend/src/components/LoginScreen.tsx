import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

type LoginScreenProps = {
  onLogin: (key: string) => void;
};

export function LoginScreen({ onLogin }: LoginScreenProps) {
  const [key, setKey] = useState('');
  const [error, setError] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!key.trim()) {
      setError('Please enter your auth key.');
      return;
    }
    setError('');
    onLogin(key.trim());
  };

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-4 text-foreground">
      <div className="w-full max-w-sm rounded-xl border border-border bg-card p-8 shadow-lg">
        <div className="text-center">
          <img src="/collaboard-logo.svg" alt="Collaboard" className="mx-auto w-80" />
          <p className="mt-3 text-sm text-muted-foreground">Enter your auth key to continue</p>
        </div>

        <form onSubmit={handleSubmit} className="mt-6 space-y-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="auth-key">Auth Key</Label>
            <Input
              id="auth-key"
              className="h-11"
              value={key}
              onChange={(e) => setKey(e.target.value)}
              placeholder="Paste your X-User-Key"
              autoFocus
            />
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <Button type="submit" className="h-11 w-full">
            Log In
          </Button>
        </form>

        <p className="mt-6 text-center text-xs text-muted-foreground">
          AI-powered kanban for humans and bots
        </p>
      </div>
    </main>
  );
}
