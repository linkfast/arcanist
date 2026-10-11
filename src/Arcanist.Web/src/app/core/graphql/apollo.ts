import { inject } from '@angular/core';
import { ApolloLink, InMemoryCache } from '@apollo/client';
import { GraphQLWsLink } from '@apollo/client/link/subscriptions';
import { getMainDefinition } from '@apollo/client/utilities';
import { provideApollo } from 'apollo-angular';
import { HttpLink } from 'apollo-angular/http';
import { Kind, OperationTypeNode } from 'graphql';
import { createClient } from 'graphql-ws';
import { AuthService } from '../auth/auth.service';

/**
 * Queries and mutations go over HTTP through Angular's HttpClient, so the auth interceptor handles tokens and
 * session recovery. Subscriptions use a WebSocket that sends the access token when it connects.
 * The panel always reads fresh data, so Apollo's cache is bypassed.
 */
export function provideArcanistApollo() {
  return provideApollo(() => {
    const auth = inject(AuthService);
    const http = inject(HttpLink).create({ uri: '/graphql' });

    const ws = new GraphQLWsLink(
      createClient({
        url: new URL('/graphql', location.href).href.replace(/^http/, 'ws'),
        lazy: true,
        connectionParams: () =>
          auth.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {},
      }),
    );

    const link = ApolloLink.split(
      ({ query }) => {
        const definition = getMainDefinition(query);
        return (
          definition.kind === Kind.OPERATION_DEFINITION &&
          definition.operation === OperationTypeNode.SUBSCRIPTION
        );
      },
      ws,
      http,
    );

    return {
      link,
      cache: new InMemoryCache(),
      defaultOptions: {
        query: { fetchPolicy: 'network-only' },
        watchQuery: { fetchPolicy: 'network-only' },
      },
    };
  });
}
