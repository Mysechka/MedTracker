-- Migration: Fix undo_confirm_dose net inventory refund calculation
-- Ensures inventory is only refunded when undoing from 'Taken' state and calculates net unrefunded debit (Debit - Credit).

create or replace function public.undo_confirm_dose(
  p_dose_event_id uuid,
  p_undone_at timestamptz default timezone('utc', now())
)
returns jsonb
language plpgsql
security invoker
set search_path = public
as $$
declare
  v_uid uuid := auth.uid();
  v_is_service boolean := (auth.role() = 'service_role');
  v_event public.dose_events%rowtype;
  v_updated public.dose_events%rowtype;
  v_course public.courses%rowtype;
  v_inventory public.inventory%rowtype;
  v_tx_id uuid;
  v_owner uuid;
  v_debited numeric(12,4) := 0;
begin
  if v_uid is null and not v_is_service then
    raise exception 'not authenticated';
  end if;

  select * into v_event
  from public.dose_events
  where id = p_dose_event_id
    and (v_is_service or user_id = v_uid)
  for update;

  if not found then
    raise exception 'dose_event not found';
  end if;

  if v_event.state not in ('Taken', 'Skipped') then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'state', v_event.state,
      'reason', 'undo only from Taken or Skipped'
    );
  end if;

  update public.dose_events
  set
    state = 'Scheduled',
    taken_at = null,
    source = null
  where id = v_event.id
    and state in ('Taken', 'Skipped')
  returning * into v_updated;

  if not found then
    return jsonb_build_object(
      'outcome', 'Rejected',
      'dose_event_id', v_event.id,
      'reason', 'lost race'
    );
  end if;

  v_owner := v_updated.user_id;

  select * into v_course from public.courses where id = v_updated.course_id;

  select * into v_inventory
  from public.inventory
  where user_id = v_owner and medication_id = v_course.medication_id
  for update;

  if not found then
    insert into public.inventory (id, user_id, medication_id, quantity_on_hand, low_stock_threshold)
    values (gen_random_uuid(), v_owner, v_course.medication_id, 0, 0)
    returning * into v_inventory;
  end if;

  if v_event.state = 'Taken' then
    select greatest(0, coalesce(sum(
      case
        when kind = 'Debit' then amount
        when kind = 'Credit' then -amount
        else 0
      end
    ), 0)) into v_debited
    from public.inventory_transactions
    where dose_event_id = v_updated.id;

    if v_debited > 0 then
      v_tx_id := gen_random_uuid();

      insert into public.inventory_transactions (
        id, user_id, inventory_id, medication_id, dose_event_id, kind, amount, note
      ) values (
        v_tx_id, v_owner, v_inventory.id, v_inventory.medication_id, v_updated.id,
        'Credit', v_debited, 'undo'
      );

      update public.inventory
      set quantity_on_hand = quantity_on_hand + v_debited
      where id = v_inventory.id;
    end if;
  end if;

  return jsonb_build_object(
    'outcome', 'Applied',
    'dose_event_id', v_updated.id,
    'state', v_updated.state,
    'transaction_id', v_tx_id,
    'quantity_on_hand', v_inventory.quantity_on_hand + coalesce(v_debited, 0)
  );
end;
$$;
