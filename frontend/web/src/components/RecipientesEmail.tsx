import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Mail, MailPlus, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { api } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";

export function RecipientesEmail() {
  const queryClient = useQueryClient();
  const recipientsQuery = useQuery({
    queryKey: ["recipients"],
    queryFn: api.recipients,
  });

  const addMutation = useMutation({
    mutationFn: (email: string) => api.addRecipient(email),
    onSuccess: (result) => {
      toast.success(
        result.alreadyExisted
          ? `${result.recipient.email} ya estaba registrado.`
          : `${result.recipient.email} recibirá los avisos de carga.`,
      );
      queryClient.invalidateQueries({ queryKey: ["recipients"] });
    },
    onError: (error: Error) => toast.error(`No se pudo dar de alta: ${error.message}`),
  });

  const removeMutation = useMutation({
    mutationFn: (id: number) => api.removeRecipient(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["recipients"] });
    },
    onError: (error: Error) => toast.error(`No se pudo eliminar: ${error.message}`),
  });

  const recipients = recipientsQuery.data?.recipients ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Mail className="size-4 text-primary" />
          Emails de notificación de "archivo cargado"
        </CardTitle>
        <CardDescription>
          Al completarse un import, el notifier envía el resumen a todos los registrados (verificable
          en Mailpit). Lista vacía = se usa el destinatario del .env.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <form
          className="flex gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            const form = event.currentTarget;
            const input = form.elements.namedItem("email") as HTMLInputElement;
            const email = input.value.trim();
            if (email) {
              addMutation.mutate(email);
              input.value = "";
            }
          }}
        >
          <Input
            name="email"
            type="email"
            required
            aria-label="Email destinatario"
            placeholder="destinatario@empresa.com"
            disabled={addMutation.isPending}
          />
          <Button type="submit" disabled={addMutation.isPending} className="shrink-0">
            {addMutation.isPending ? (
              <MailPlus className="animate-pulse" />
            ) : (
              <Plus />
            )}
            Alta
          </Button>
        </form>

        {recipientsQuery.isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-2/3" />
          </div>
        ) : recipientsQuery.isError ? (
          <p className="text-sm text-destructive">
            No se pudo leer la tabla notification_recipient ({(recipientsQuery.error as Error).message}).
          </p>
        ) : recipients.length === 0 ? (
          <p className="rounded-lg border border-dashed p-4 text-center text-sm text-muted-foreground">
            Sin destinatarios registrados: el notifier usa el fallback de configuración.
          </p>
        ) : (
          <ul className="divide-y rounded-lg border" data-testid="recipients-list">
            {recipients.map((recipient) => (
              <li key={recipient.id} className="flex items-center justify-between px-3 py-2 text-sm">
                <span className="font-mono">{recipient.email}</span>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-7 w-7 text-muted-foreground hover:text-destructive"
                  aria-label={`Eliminar ${recipient.email}`}
                  disabled={removeMutation.isPending}
                  onClick={() => removeMutation.mutate(recipient.id)}
                >
                  <Trash2 />
                </Button>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
